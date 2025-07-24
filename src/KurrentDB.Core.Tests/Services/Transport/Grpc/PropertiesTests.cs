// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.Streams;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.Core.Tests.Services.Transport.Grpc.StreamsTests;
using KurrentDB.Protocol.V2;
using NUnit.Framework;
using MetadataConstants = KurrentDB.Core.Services.Transport.Grpc.Constants.Metadata;
using PropertiesConstants = KurrentDB.Core.Services.Transport.Grpc.Constants.Properties;

namespace KurrentDB.Core.Tests.Services.Transport.Grpc;

[TestFixture]
public class PropertiesTests : GrpcSpecification<LogFormat.V2, string> {
	static StreamIdentifier CreateStreamIdentifier(string stream) => new() {
		StreamName = ByteString.CopyFromUtf8(stream)
	};

	async Task AppendV1(string stream, string contentType, string metadataJson) {
		using var call = StreamsClient.Append(GetCallOptions(AdminCredentials));
		await call.RequestStream.WriteAsync(new() {
			Options = new() {
				StreamIdentifier = CreateStreamIdentifier(stream),
				Any = new(),
			}
		});

		await call.RequestStream.WriteAsync(new() {
			ProposedMessage = new() {
				Data = ByteString.CopyFromUtf8("test-data"),
				Id = Uuid.NewUuid().ToDto(),
				CustomMetadata = ByteString.CopyFromUtf8(metadataJson),
				Metadata = {
					{ MetadataConstants.Type, "test-type" },
					{ MetadataConstants.ContentType, contentType },
				}
			}
		});
		await call.RequestStream.CompleteAsync();

		var response = await call.ResponseAsync;
		if (response.ResultCase is not AppendResp.ResultOneofCase.Success) {
			throw new Exception($"Append V1 failed {response.ResultCase}");
		}
	}

	async Task AppendV2(string stream, string dataFormat, MapField<string, Protobuf.DynamicValue> extraProperties) {
		MapField<string, Protobuf.DynamicValue> properties = new() {
			extraProperties,
			{ PropertiesConstants.EventTypeKey, new() { StringValue = "test-type" } },
			{ PropertiesConstants.DataFormatKey, new() { StringValue = dataFormat } },
		};

		var request = new MultiStreamAppendRequest() {
			Input = {
				new AppendStreamRequest() {
					ExpectedRevision = -1,
					Stream = stream,
					Records = {
						new AppendRecord() {
							RecordId = Guid.NewGuid().ToString(),
							Data = ByteString.CopyFromUtf8("test-data"),
							Properties = { properties },
						}
					}
				}
			}
		};

		using var call = StreamsClientV2.MultiStreamAppendAsync(request, GetCallOptions(AdminCredentials));

		var response = await call.ResponseAsync;
		if (response.ResultCase is not MultiStreamAppendResponse.ResultOneofCase.Success) {
			throw new Exception($"Append V2 failed {response.ResultCase}");
		}
	}

	private async Task<ReadResp> ReadSingleEventV1(string stream) {
		var request = new ReadReq() {
			Options = new() {
				Count = 1,
				Stream = new() {
					StreamIdentifier = CreateStreamIdentifier(stream),
					Start = new(),
				},
				UuidOption = new() { Structured = new() },
				ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
				NoFilter = new(),
				ControlOption = new() {
					Compatibility = 21,
				},
			},
		};

		using var call = StreamsClient.Read(request);
		var readResponses = await call.ResponseStream.ReadAllAsync().ToArrayAsync();
		// discard the proto messages that are not events
		return readResponses.Single(x => x.Event is not null);
	}

	protected override Task Given() => Task.CompletedTask;

	protected override Task When() => Task.CompletedTask;

	[TestCase(MetadataConstants.ContentTypes.ApplicationJson)]
	[TestCase(MetadataConstants.ContentTypes.ApplicationOctetStream)]
	public async Task write_with_v1_then_read_with_v1(string contentType) {
		var stream = $"write_with_v1_read_with_v1_{contentType}-{Guid.NewGuid()}";

		var logRecordMetadataJson = """
			{
			  "my-number": 42,
			  "my-string": "hello world"
			}
			""";

		await AppendV1(
			stream: stream,
			contentType: contentType,
			metadataJson: logRecordMetadataJson);

		var evt = (await ReadSingleEventV1(stream)).Event.Event;

		// content-type is preserved
		var protocolMetadata = evt.Metadata;
		Assert.AreEqual(3, protocolMetadata.Count);
		Assert.True(protocolMetadata.TryGetValue(MetadataConstants.Created, out _));
		Assert.AreEqual("test-type", protocolMetadata[MetadataConstants.Type]);
		Assert.AreEqual(contentType, protocolMetadata[MetadataConstants.ContentType]);

		// log record metadata comes out the same as it went in
		Assert.AreEqual(logRecordMetadataJson, evt.CustomMetadata.ToStringUtf8());
	}

	[TestCase(PropertiesConstants.DataFormats.Json, MetadataConstants.ContentTypes.ApplicationJson)]
	[TestCase(PropertiesConstants.DataFormats.Avro, MetadataConstants.ContentTypes.ApplicationOctetStream)]
	[TestCase(PropertiesConstants.DataFormats.Bytes, MetadataConstants.ContentTypes.ApplicationOctetStream)]
	[TestCase(PropertiesConstants.DataFormats.Protobuf, MetadataConstants.ContentTypes.ApplicationOctetStream)]
	public async Task write_with_v2_then_read_with_v1(string dataFormat, string expectedContentType) {
		var stream = $"write_with_v2_read_with_v1_{dataFormat}-{Guid.NewGuid()}";

		var now = new DateTime(2025, 07, 14, 05, 05, 05, DateTimeKind.Utc);
		await AppendV2(stream, dataFormat, new() {
			{ "my-null", new() { NullValue = NullValue.NullValue } },
			{ "my-int32", new() { Int32Value = 32 } },
			{ "my-int64", new() { Int64Value = 64 } },
			{ "my-bytes", new() { BytesValue = ByteString.CopyFromUtf8("utf8-bytes") } },
			{ "my-double", new() { DoubleValue = 123.4 } },
			{ "my-float", new() { FloatValue = 567.8f } },
			{ "my-string", new() { StringValue = "hello-world" } },
			{ "my-boolean", new() { BooleanValue = true } },
			{ "my-timestamp", new() { TimestampValue = Timestamp.FromDateTime(now) } },
			{ "my-duration", new() { DurationValue = Duration.FromTimeSpan(TimeSpan.FromSeconds(121)) } },
		});

		var evt = (await ReadSingleEventV1(stream)).Event.Event;

		// dataFormat is translated to correct content-type
		var protocolMetadata = evt.Metadata;
		Assert.AreEqual(3, protocolMetadata.Count);
		Assert.True(protocolMetadata.TryGetValue(MetadataConstants.Created, out _));
		Assert.AreEqual("test-type", protocolMetadata[MetadataConstants.Type]);
		Assert.AreEqual(expectedContentType, protocolMetadata[MetadataConstants.ContentType]);

		// todo: should probably prefer non strings for the numbers and bool.
		// todo: doubt the timestamp formatting including \u002B is correct
		// todo: consider formatting timestamp like this since it doesn't have any timezone info: "2025-07-14T05:05:05.0000000Z"

		// log record metadata contains the properties
		var expectedMetadata = $$"""
			{
			  "my-null":             null,
			  "my-int32":            "32",
			  "my-int64":            "64",
			  "my-bytes":            "{{Convert.ToBase64String(Encoding.UTF8.GetBytes("utf8-bytes"))}}",
			  "my-double":           "123.4",
			  "my-float":            "567.8",
			  "my-string":           "hello-world",
			  "my-boolean":          "True",
			  "my-timestamp":        "2025-07-14T05:05:05.0000000\u002B00:00",
			  "my-duration":         "00:02:01",
			  "$schema.data-format": "{{dataFormat}}",
			  "$schema.name":        "test-type"
			}
			""".Replace(" ", "").Replace(Environment.NewLine, "");

		var actual = evt.CustomMetadata.ToStringUtf8();
		Assert.AreEqual(expectedMetadata, actual);
	}

	[Test]
	public void write_v2_with_invalid_data_format() {
		var dataFormat = "something-else";
		var stream = $"write_v2_with_invalid_data_format_{Guid.NewGuid()}";

		var ex = Assert.ThrowsAsync<RpcException>(() => AppendV2(stream, dataFormat, []));

		Assert.AreEqual("Data format 'something-else' is not supported", ex!.Status.Detail);
		Assert.AreEqual(StatusCode.InvalidArgument, ex.StatusCode);
	}
}
