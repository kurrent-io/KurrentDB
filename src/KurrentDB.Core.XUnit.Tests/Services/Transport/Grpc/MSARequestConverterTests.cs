// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Security.Claims;
using System.Text;
using System.Threading;
using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.Protobuf.Server;
using KurrentDB.Protocol.V2;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services.Transport.Grpc;

public class MSARequestConverterTests {
	const int TestMaxAppendSize = 400;
	const int TestMaxAppendEventSize = 300;

	private readonly MSARequestConverter _sut = new(
		maxAppendSize: TestMaxAppendSize,
		maxAppendEventSize: TestMaxAppendEventSize);

	[Fact]
	public void can_ConvertRequests_with_multiple_requests() {
		// given
		var event1Id = Guid.NewGuid();
		var event2Id = Guid.NewGuid();

		var input = new AppendStreamRequest[] {
			new() {
				Stream = "stream-a",
				Records = {
					new AppendRecord {
						RecordId = event1Id.ToString(),
						Properties = {
							{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("my-event-type") } },
							{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("json") } },
						}
					},
				},
			},
			new() {
				Stream = "stream-b",
				Records = {
					new AppendRecord {
						RecordId = event2Id.ToString(),
						Properties = {
							{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("my-event-type") } },
							{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("json") } },
						}
					},
				},
			},
		};

		// when
		var output = _sut.ConvertRequests(
			appendStreamRequests: input,
			envelope: IEnvelope.NoOp,
			user: new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "the-user")])),
			token: CancellationToken.None);

		// then
		Assert.Equal(IEnvelope.NoOp, output.Envelope);
		Assert.Equal("the-user", output.User.Identity!.Name);
		Assert.True(output.RequireLeader);

		Assert.Equal(["stream-a", "stream-b"], output.EventStreamIds.Span);
		Assert.Equal([0, 1], output.EventStreamIndexes!.Value.Span);
		Assert.Equal([-2, -2], output.ExpectedVersions.Span);

		Assert.Equal(2, output.Events.Length);
		Assert.Equal(event1Id, output.Events.Span[0].EventId);
		Assert.Equal(event2Id, output.Events.Span[1].EventId);
	}

	[Theory]
	[InlineData(null)]
	[InlineData(-4)] // stream exists
	[InlineData(-2)] // any
	[InlineData(-1)] // no stream
	[InlineData(0)]
	[InlineData(1)]
	public void can_ConvertRequests_with_expected_revision(int? expectedRevision) {
		// given
		var input = new AppendStreamRequest[] {
			new() {
				Stream = "stream-a",
				Records = {
					new AppendRecord() {
						Properties = {
							{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("the-type") } },
							{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("json") } },
						},
					},
				 },
			},
		};

		if (expectedRevision.HasValue) {
			input[0].ExpectedRevision = expectedRevision.Value;
		}

		// when
		var output = _sut.ConvertRequests(
			appendStreamRequests: input,
			envelope: IEnvelope.NoOp,
			user: new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "the-user")])),
			token: CancellationToken.None);

		// then
		Assert.Equal(
			expectedRevision.HasValue
				? [ expectedRevision.Value ]
				: [ -2 ],
			output.ExpectedVersions.Span);
	}

	[Fact]
	public void ConvertRequests_throws_when_converting_no_requests() {
		// given
		var input = Array.Empty<AppendStreamRequest>();

		// when
		var ex = Assert.Throws<RpcException>(() => {
			_sut.ConvertRequests(
				appendStreamRequests: input,
				envelope: IEnvelope.NoOp,
				user: new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "the-user")])),
				token: CancellationToken.None);
		});

		// then
		Assert.Equal("At least one AppendStreamRequest must be present", ex.Status.Detail);
		Assert.Equal(StatusCode.InvalidArgument, ex.Status.StatusCode);
	}

	[Fact]
	public void ConvertRequests_throws_when_AppendStreamRequest_has_no_events() {
		// given
		var input = new AppendStreamRequest[] {
			new() { Stream = "stream-a" },
		};

		// when
		var ex = Assert.Throws<RpcException>(() => {
			_sut.ConvertRequests(
				appendStreamRequests: input,
				envelope: IEnvelope.NoOp,
				user: new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "the-user")])),
				token: CancellationToken.None);
		});

		// then
		Assert.Equal("Write to stream \"stream-a\" does not have any records", ex.Status.Detail);
		Assert.Equal(StatusCode.InvalidArgument, ex.Status.StatusCode);
	}

	[Fact]
	public void ConvertRequests_throws_when_max_append_event_size_is_exceeded() {
		// given
		var eventId = Guid.NewGuid();
		var input = new AppendStreamRequest[] {
			new() {
				Stream = "stream-a",
				Records = {
					new AppendRecord() {
						RecordId = eventId.ToString(),
						Data = ByteString.CopyFrom(new byte[TestMaxAppendEventSize]),
						Properties = {
							{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("the-type") } },
							{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("json") } },
						},
					}
				},
			},
		};

		// when
		var ex = Assert.Throws<RpcException>(() => {
			_sut.ConvertRequests(
				appendStreamRequests: input,
				envelope: IEnvelope.NoOp,
				user: new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "the-user")])),
				token: CancellationToken.None);
		});

		// then
		Assert.Equal($"Event with Id: {eventId}, Size: 316, exceeds Maximum Append Event Size of 300.", ex.Status.Detail);
		Assert.Equal(StatusCode.InvalidArgument, ex.Status.StatusCode);
	}

	[Fact]
	public void ConvertRequests_throws_when_max_append_size_is_exceeded() {
		// given
		var input = new AppendStreamRequest[] {
			new() {
				Stream = "stream-a",
				Records = {
					new AppendRecord() {
						Data = ByteString.CopyFrom(new byte[TestMaxAppendSize / 2]),
						Properties = {
							{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("the-type") } },
							{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("json") } },
						},
					}
				},
			},
			new() {
				Stream = "stream-b",
				Records = {
					new AppendRecord() {
						Data = ByteString.CopyFrom(new byte[TestMaxAppendSize / 2]),
						Properties = {
							{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("the-type") } },
							{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("json") } },
						},
					}
				},
			},
		};

		// when
		var ex = Assert.Throws<RpcException>(() => {
			_sut.ConvertRequests(
				appendStreamRequests: input,
				envelope: IEnvelope.NoOp,
				user: new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "the-user")])),
				token: CancellationToken.None);
		});

		// then
		Assert.Equal("Maximum Append Size of 400 Exceeded.", ex.Status.Detail);
		Assert.Equal(StatusCode.InvalidArgument, ex.Status.StatusCode);
	}

	// note this as a current limitation, not necessarily permanent.
	// the core supports this already, the grpc service needs additional work to
	// 1. make sure there are as many AppendStreamResponses as AppendStreamRequests on success
	// 2. check for internal consistency of expected versions in the request
	// 3. find a way of handling if the request has expected version any for the first occurrence
	//    of a stream and then expected version specific for a later occurrence.
	[Fact]
	public void ConvertRequests_throws_when_stream_present_twice() {
		// given
		static AppendRecord CreateRecord() => new() {
			Properties = {
				{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("the-type") } },
				{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("json") } },
			},
		};
		var input = new AppendStreamRequest[] {
			new() { Stream = "stream-a", Records = { CreateRecord() } },
			new() { Stream = "stream-b", Records = { CreateRecord() } },
			new() { Stream = "stream-a", Records = { CreateRecord() } },
		};

		// when
		var ex = Assert.Throws<RpcException>(() => {
			_sut.ConvertRequests(
				appendStreamRequests: input,
				envelope: IEnvelope.NoOp,
				user: new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "the-user")])),
				token: CancellationToken.None);
		});

		// then
		Assert.Equal("Two AppendStreamRequests for one stream is not currently supported", ex.Status.Detail);
		Assert.Equal(StatusCode.InvalidArgument, ex.Status.StatusCode);
	}

	[Theory]
	[InlineData("json", true)]
	[InlineData("avro", false)]
	public void can_ConvertRecord(string dataFormat, bool expectedIsJson) {
		// given
		var recordId = Guid.NewGuid();
		var input = new AppendRecord {
			RecordId = recordId.ToString(),
			Data = ByteString.CopyFromUtf8("the-data"),
			Properties = {
				{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("my-event-type") } },
				{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8(dataFormat) } },
				{ Constants.Properties.LegacyMetadata, new() { BytesValue = ByteString.CopyFromUtf8("""{ "foo": "bar" }""") } },
				{ "property1", new() { BooleanValue = true } },
				{ "property2", new() { StringValue = "test" } },
				{ "property3", new() { Int32Value = 1234 } },
			},
		};

		// when
		var output = _sut.ConvertRecord(input);

		// then
		Assert.Equal(recordId, output.EventId);
		Assert.Equal("my-event-type", output.EventType);
		Assert.Equal(expectedIsJson, output.IsJson);
		Assert.Equal(Encoding.UTF8.GetBytes("the-data"), output.Data);
		Assert.Equal(Encoding.UTF8.GetBytes("""{ "foo": "bar" }"""), output.Metadata);

		var properties = Properties.Parser.ParseFrom(output.Properties);

		Assert.Equal(3, properties.PropertiesValues.Count);

		properties.PropertiesValues.TryGetValue("property1", out var property1);
		Assert.True(property1!.BooleanValue);

		properties.PropertiesValues.TryGetValue("property2", out var property2);
		Assert.Equal("test", property2!.StringValue);

		properties.PropertiesValues.TryGetValue("property3", out var property3);
		Assert.Equal(1234, property3!.Int32Value);
	}

	[Fact]
	public void can_ConvertRecord_with_minimal_fields() {
		// data and metadata can be blank
		// given
		var input = new AppendRecord {
			Properties = {
				{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("my-event-type") } },
				{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("avro") } },
			},
		};

		// when
		var output = _sut.ConvertRecord(input);

		// then
		Assert.NotEqual(Guid.Empty, output.EventId);
		Assert.Equal("my-event-type", output.EventType);
		Assert.False(output.IsJson);
		Assert.Empty(output.Data);
		Assert.Empty(output.Metadata);
	}

	[Theory]
	[InlineData("")]
	[InlineData("junk")]
	public void ConvertRecord_throws_when_record_has_invalid_id(string recordId) {
		// given
		var input = new AppendRecord {
			RecordId = recordId,
			Properties = {
				{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("my-event-type") } },
				{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("json") } },
			},
		};

		// when
		var ex = Assert.Throws<RpcException>(() => {
			_sut.ConvertRecord(input);
		});

		// then
		Assert.Equal($"Could not parse RecordId \"{recordId}\" to GUID", ex.Status.Detail);
		Assert.Equal(StatusCode.InvalidArgument, ex.Status.StatusCode);
		Assert.Empty(ex.Trailers);
	}


	[Theory]
	[InlineData(Constants.Properties.EventType)]
	[InlineData(Constants.Properties.DataFormat)]
	public void ConvertRecord_throws_when_record_has_missing_required_property(string missingProperty) {
		// given
		var input = new AppendRecord {
			Properties = {
				{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("my-event-type") } },
				{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("json") } },
			},
		};

		input.Properties.Remove(missingProperty);

		// when
		var ex = Assert.Throws<RpcException>(() => {
			_sut.ConvertRecord(input);
		});

		// then
		Assert.Equal($"Required Property '{missingProperty}' is missing", ex.Status.Detail);
		Assert.Equal(StatusCode.InvalidArgument, ex.Status.StatusCode);
		Assert.Collection(
			ex.Trailers,
			x => {
				Assert.Equal(Constants.Exceptions.ExceptionKey, x.Key);
				Assert.Equal(Constants.Exceptions.MissingRequiredProperty, x.Value);
			},
			x => {
				Assert.Equal(Constants.Exceptions.RequiredProperties, x.Key);
				Assert.Equal(Constants.Properties.RequiredProperties, x.Value);
			});
	}

	[Theory]
	[InlineData(Constants.Properties.EventType)]
	[InlineData(Constants.Properties.DataFormat)]
	public void ConvertRecord_throws_when_record_has_required_property_with_wrong_type(string wrongProperty) {
		// given
		var input = new AppendRecord {
			Properties = {
				{ Constants.Properties.EventType, new() { BytesValue = ByteString.CopyFromUtf8("my-event-type") } },
				{ Constants.Properties.DataFormat, new() { BytesValue = ByteString.CopyFromUtf8("json") } },
			},
		};

		input.Properties.Remove(wrongProperty);
		input.Properties.Add(wrongProperty, new() { Int64Value = 123 });

		// when
		var ex = Assert.Throws<RpcException>(() => {
			_sut.ConvertRecord(input);
		});

		// then
		Assert.Equal($"Required Property '{wrongProperty}' is missing", ex.Status.Detail);
		Assert.Equal(StatusCode.InvalidArgument, ex.Status.StatusCode);
		Assert.Collection(
			ex.Trailers,
			x => {
				Assert.Equal(Constants.Exceptions.ExceptionKey, x.Key);
				Assert.Equal(Constants.Exceptions.MissingRequiredProperty, x.Value);
			},
			x => {
				Assert.Equal(Constants.Exceptions.RequiredProperties, x.Key);
				Assert.Equal(Constants.Properties.RequiredProperties, x.Value);
			});
	}
}
