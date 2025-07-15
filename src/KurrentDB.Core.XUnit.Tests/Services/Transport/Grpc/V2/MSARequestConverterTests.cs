// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.Core.Services.Transport.Grpc.V2;
using KurrentDB.Protobuf.Server;
using KurrentDB.Protocol.V2;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services.Transport.Grpc;

public class MSARequestConverterTests {
	const int TestChunkSize = 10_000;
	const int TestMaxAppendSize = 600;
	const int TestMaxAppendEventSize = 500;

	MultiStreamAppendConverter Sut { get; } = new(TestChunkSize, TestMaxAppendSize, TestMaxAppendEventSize);

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
							{ Constants.Properties.EventType, new() { StringValue = "my-event-type" } },
							{ Constants.Properties.DataFormat, new() { StringValue = "json" } },
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
							{ Constants.Properties.EventType, new() { StringValue = "my-event-type" } },
							{ Constants.Properties.DataFormat, new() { StringValue = "json" } },
						}
					},
				},
			},
		};

		// when
		var output = Sut.ConvertToEvents(input);

		// then
		Assert.Equal(["stream-a", "stream-b"], output.StreamIds.AsSpan());
		Assert.Equal([0, 1], output.StreamIndexes.AsSpan());
		Assert.Equal([-2, -2], output.ExpectedVersions.AsSpan());

		Assert.Equal(2, output.Events.Length);
		Assert.Equal(event1Id, output.Events.AsSpan()[0].EventId);
		Assert.Equal(event2Id, output.Events.AsSpan()[1].EventId);
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
					new AppendRecord {
						Properties = {
							{ Constants.Properties.EventType, new() { StringValue = "the-type" } },
							{ Constants.Properties.DataFormat, new() { StringValue = "json" } },
						},
					},
				},
			},
		};

		if (expectedRevision.HasValue) {
			input[0].ExpectedRevision = expectedRevision.Value;
		}

		// when
		var output = Sut.ConvertToEvents(input);

		// then
		Assert.Equal(
			expectedRevision.HasValue
				? [expectedRevision.Value]
				: [-2],
			output.ExpectedVersions.AsSpan());
	}

	[Fact]
	public void ConvertRequests_throws_when_converting_no_requests() {
		// given
		var input = Array.Empty<AppendStreamRequest>();

		// when
		var ex = Assert.Throws<RpcException>(() => Sut.ConvertToEvents(requests: input));

		// then
		Assert.Equal("At least one AppendStreamRequest must be present", ex.Status.Detail);
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
					new AppendRecord {
						RecordId = eventId.ToString(),
						Data = ByteString.CopyFrom(new byte[TestMaxAppendEventSize]),
						Properties = {
							{ Constants.Properties.EventType, new() { StringValue = "the-type" } },
							{ Constants.Properties.DataFormat, new() { StringValue = "json" } },
						},
					}
				},
			},
		};

		// when
		var ex = Assert.Throws<RpcException>(() => Sut.ConvertToEvents(input));

		// then
		Assert.Equal(StatusCode.InvalidArgument, ex.Status.StatusCode);
		Assert.Equal(Constants.Exceptions.MaximumAppendEventSizeExceeded, ex.Trailers.Get(Constants.Exceptions.ExceptionKey)?.Value);
		Assert.Equal(TestMaxAppendEventSize.ToString(), ex.Trailers.Get(Constants.Exceptions.MaximumAppendEventSize)?.Value);
	}

	[Fact]
	public void ConvertRequests_throws_when_max_append_size_is_exceeded() {
		// given
		var input = new AppendStreamRequest[] {
			new() {
				Stream = "stream-a",
				Records = {
					new AppendRecord {
						Data = ByteString.CopyFrom(new byte[TestMaxAppendSize / 2]),
						Properties = {
							{ Constants.Properties.EventType, new() { StringValue = "the-type" } },
							{ Constants.Properties.DataFormat, new() { StringValue = "json" } },
						},
					}
				},
			},
			new() {
				Stream = "stream-b",
				Records = {
					new AppendRecord {
						Data = ByteString.CopyFrom(new byte[TestMaxAppendSize / 2]),
						Properties = {
							{ Constants.Properties.EventType, new() { StringValue = "the-type" } },
							{ Constants.Properties.DataFormat, new() { StringValue = "json" } },
						},
					}
				},
			},
		};

		// when
		var ex = Assert.Throws<RpcException>(() => Sut.ConvertToEvents(requests: input));

		// then
		Assert.Equal(StatusCode.InvalidArgument, ex.Status.StatusCode);
		Assert.Equal(Constants.Exceptions.MaximumAppendSizeExceeded, ex.Trailers.Get(Constants.Exceptions.ExceptionKey)?.Value);
		Assert.Equal(TestMaxAppendSize.ToString(), ex.Trailers.Get(Constants.Exceptions.MaximumAppendSize)?.Value);
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
				{ Constants.Properties.EventType, new() { StringValue = "my-event-type" } },
				{ Constants.Properties.DataFormat, new() { StringValue = dataFormat } },
				{ "property1", new() { BooleanValue = true } },
				{ "property2", new() { StringValue = "test" } },
				{ "property3", new() { Int32Value = 1234 } },
			},
		};

		// when
		var output = MultiStreamAppendConverter.ConvertToEvent(input);

		// then
		Assert.Equal(recordId, output.EventId);
		Assert.Equal("my-event-type", output.EventType);
		Assert.Equal(expectedIsJson, output.IsJson);
		Assert.Equal("the-data"u8.ToArray(), output.Data);
		Assert.Equal([], output.Metadata);

		var properties = Properties.Parser.ParseFrom(output.Properties);

		Assert.Equal(5, properties.PropertiesValues.Count);

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
				{ Constants.Properties.EventType, new() { StringValue = "my-event-type" } },
				{ Constants.Properties.DataFormat, new() { StringValue = "avro" } },
			},
		};

		// var expectedMetadata = ProtoJsonSerializer.Default.Serialize(new Properties { PropertiesValues = { input.Properties } }).ToArray();

		// when
		var output = MultiStreamAppendConverter.ConvertToEvent(input);

		// then
		Assert.NotEqual(Guid.Empty, output.EventId);
		Assert.Equal("my-event-type", output.EventType);
		Assert.False(output.IsJson);
		Assert.Empty(output.Data);
		Assert.Equal([],output.Metadata);
	}

	[Theory]
	[InlineData("")]
	[InlineData("junk")]
	public void ConvertRecord_throws_when_record_has_invalid_id(string recordId) {
		// given
		var input = new AppendRecord {
			RecordId = recordId,
			Properties = {
				{ Constants.Properties.EventType, new() { StringValue = "my-event-type" } },
				{ Constants.Properties.DataFormat, new() { StringValue = "json" } },
			},
		};

		// when
		var ex = Assert.Throws<RpcException>(() => MultiStreamAppendConverter.ConvertToEvent(input));

		// then
		Assert.Equal($"Could not parse RecordId '{recordId}' to GUID", ex.Status.Detail);
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
				{ Constants.Properties.EventType, new() { StringValue = "my-event-type" } },
				{ Constants.Properties.DataFormat, new() { StringValue = "json" } },
			},
		};

		input.Properties.Remove(missingProperty);

		// when
		var ex = Assert.Throws<RpcException>(() => MultiStreamAppendConverter.ConvertToEvent(input));

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
				{ Constants.Properties.EventType, new() { StringValue = "my-event-type" } },
				{ Constants.Properties.DataFormat, new() { StringValue = "json" } },
			},
		};

		input.Properties.Remove(wrongProperty);
		input.Properties.Add(wrongProperty, new() { Int64Value = 123 });

		// when
		var ex = Assert.Throws<RpcException>(() => {
			MultiStreamAppendConverter.ConvertToEvent(input);
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
