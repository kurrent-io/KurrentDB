// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Protobuf.Server;
using KurrentDB.Protocol;
using Xunit;
using Duration = Google.Protobuf.WellKnownTypes.Duration;

namespace KurrentDB.Core.XUnit.Tests.Services.Transport.Grpc;

public class MetadataHelpersTests {
	private readonly DateTime _timeStamp = DateTime.Now;
	private readonly string _eventType = "test-event";
	private readonly string _streamName = "test-stream";

	private string TicksSinceEpoch => (_timeStamp - DateTime.UnixEpoch).Ticks.ToString();

	private EventRecord CreateEventRecord(string contentType, byte[] properties) {
		var flags = contentType is Constants.Metadata.ContentTypes.ApplicationJson
			? PrepareFlags.IsJson | PrepareFlags.SingleWrite
			: PrepareFlags.SingleWrite;
		return new EventRecord(0, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0, _streamName, -1, _timeStamp,
			flags, _eventType, "{\"foo\":\"bar\"}"u8.ToArray(), "test-metadata"u8.ToArray(), properties);
	}

	[Theory]
	[InlineData(Constants.Metadata.ContentTypes.ApplicationOctetStream)]
	[InlineData(Constants.Metadata.ContentTypes.ApplicationJson)]
	public void when_mapping_event_record_with_no_content_type_property_should_use_is_json_for_content_type(string contentType) {
		var customKey = "custom-property";
		var customValue = "custom-value";
		var properties = new Properties {
			PropertiesValues = {
				{
					customKey,
					new DynamicValue { BytesValue = ByteString.CopyFromUtf8(customValue) }
				}
			}
		};

		var eventRecord = CreateEventRecord(contentType, properties.ToByteArray());

		var map = new MapField<string, string>();
		map.AddGrpcMetadataFrom(eventRecord);

		Assert.Equal(4, map.Count);
		Assert.Equal(contentType, map[Constants.Metadata.ContentType]);
		Assert.Equal(_eventType, map[Constants.Metadata.Type]);
		Assert.Equal(TicksSinceEpoch, map[Constants.Metadata.Created]);
		Assert.Equal(customValue, map[customKey]);
	}

	[Theory]
	[InlineData(Constants.Metadata.ContentTypes.ApplicationOctetStream)]
	[InlineData(Constants.Metadata.ContentTypes.ApplicationJson)]
	[InlineData("application/custom")]
	public void when_mapping_event_record_with_content_type_property_should_use_property_for_content_type(string contentType) {
		var properties = new Properties {
			PropertiesValues = {
				{
					Constants.Metadata.ContentType,
					new DynamicValue { BytesValue = ByteString.CopyFromUtf8(contentType) }
				}
			}
		};
		var eventRecord = CreateEventRecord(contentType, properties.ToByteArray());

		var map = new MapField<string, string>();
		map.AddGrpcMetadataFrom(eventRecord);

		Assert.Equal(3, map.Count);
		Assert.Equal(contentType, map[Constants.Metadata.ContentType]);
		Assert.Equal(_eventType, map[Constants.Metadata.Type]);
		Assert.Equal(TicksSinceEpoch, map[Constants.Metadata.Created]);
	}

	[Fact]
	public void when_mapping_event_record_with_custom_properties_should_return_property() {
		var contentType = Constants.Metadata.ContentTypes.ApplicationJson;
		var customKey = "custom-property";
		var customValue = "custom-value";
		var properties = new Properties {
			PropertiesValues = {
				{
					customKey, new DynamicValue { BytesValue = ByteString.CopyFromUtf8(customValue) }
				}
			}
		};
		var eventRecord = CreateEventRecord(contentType, properties.ToByteArray());

		var map = new MapField<string, string>();
		map.AddGrpcMetadataFrom(eventRecord);

		Assert.Equal(4, map.Count);
		Assert.Equal(contentType, map[Constants.Metadata.ContentType]);
		Assert.Equal(_eventType, map[Constants.Metadata.Type]);
		Assert.Equal(TicksSinceEpoch, map[Constants.Metadata.Created]);
		Assert.Equal(customValue, map[customKey]);
	}

	[Fact]
	public void when_mapping_event_record_with_properties_should_only_map_bytes_value_properties() {
		var contentType = Constants.Metadata.ContentTypes.ApplicationJson;
		var customKey = "custom-property";
		var customValue = "custom-value";
		var properties = new Properties {
			PropertiesValues = {
				{ customKey, new DynamicValue { BytesValue = ByteString.CopyFromUtf8(customValue) }},
				{ "bool-key", new DynamicValue { BooleanValue = true }},
				{ "double-key", new DynamicValue { DoubleValue = 1 }},
				{ "duration-key", new DynamicValue { DurationValue = Duration.FromTimeSpan(TimeSpan.FromMinutes(2)) }},
				{ "int-key", new DynamicValue { Int32Value = 2 }},
				{ "float-key", new DynamicValue { FloatValue = 3.0f }},
				{ "long-key", new DynamicValue { Int64Value = 4 }},
				{ "null-key", new DynamicValue { NullValue = new NullValue() }},
				{ "string-key", new DynamicValue { StringValue = "test" }},
				{ "timestamp-key", new DynamicValue { TimestampValue = Timestamp.FromDateTime(DateTime.UtcNow) }},
			}
		};
		var eventRecord = CreateEventRecord(contentType, properties.ToByteArray());

		var map = new MapField<string, string>();
		map.AddGrpcMetadataFrom(eventRecord);

		Assert.Equal(4, map.Count);
		Assert.Equal(contentType, map[Constants.Metadata.ContentType]);
		Assert.Equal(_eventType, map[Constants.Metadata.Type]);
		Assert.Equal(TicksSinceEpoch, map[Constants.Metadata.Created]);
		Assert.Equal(customValue, map[customKey]);
	}

	[Theory]
	[InlineData(Constants.Metadata.ContentTypes.ApplicationOctetStream)]
	[InlineData(Constants.Metadata.ContentTypes.ApplicationJson)]
	public void when_parsing_metadata_with_json_or_octet_content_types_it_should_not_include_content_type_or_event_type_in_properties(string contentType) {
		var map = new MapField<string, string> {
			{ Constants.Metadata.Type, _eventType },
			{ Constants.Metadata.ContentType, contentType}
		};

		var parsed = MetadataHelpers.ParseGrpcMetadata(map);
		Assert.Equal(contentType == Constants.Metadata.ContentTypes.ApplicationJson, parsed.isJson);
		Assert.Equal(_eventType, parsed.eventType);
		Assert.Empty(parsed.properties);
	}

	[Fact]
	public void when_parsing_metadata_with_custom_content_types_it_should_include_content_type_in_properties() {
		var customContentType = "application/custom";
		var map = new MapField<string, string> {
			{ Constants.Metadata.Type, _eventType },
			{ Constants.Metadata.ContentType, customContentType }
		};

		var parsed = MetadataHelpers.ParseGrpcMetadata(map);
		Assert.False(parsed.isJson);
		Assert.Equal(_eventType, parsed.eventType);

		var properties = Properties.Parser.ParseFrom(parsed.properties);
		Assert.Single(properties.PropertiesValues);
		Assert.Equal(customContentType, properties.PropertiesValues[Constants.Metadata.ContentType].BytesValue.ToStringUtf8());
	}

	[Fact]
	public void when_parsing_metadata_with_custom_properties_it_should_include_them_in_properties() {
		var customContentType = "application/custom";
		var customKey = "custom-key";
		var customValue = "custom-value";
		var map = new MapField<string, string> {
			{ Constants.Metadata.Type, _eventType },
			{ Constants.Metadata.ContentType, customContentType },
			{ customKey, customValue }
		};

		var parsed = MetadataHelpers.ParseGrpcMetadata(map);
		Assert.False(parsed.isJson);
		Assert.Equal(_eventType, parsed.eventType);

		var properties = Properties.Parser.ParseFrom(parsed.properties);
		Assert.Equal(2, properties.PropertiesValues.Count);
		Assert.Equal(customContentType, properties.PropertiesValues[Constants.Metadata.ContentType].BytesValue.ToStringUtf8());
		Assert.Equal(customValue, properties.PropertiesValues[customKey].BytesValue.ToStringUtf8());
	}

	[Fact]
	public void when_parsing_metadata_with_no_event_type_it_should_throw_an_error() {
		var map = new MapField<string, string> {
			{ Constants.Metadata.ContentType, Constants.Metadata.ContentTypes.ApplicationJson },
		};

		var ex = Assert.Throws<RpcException>(() => MetadataHelpers.ParseGrpcMetadata(map));
		Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
	}

	[Fact]
	public void when_parsing_metadata_with_no_content_type_it_should_throw_an_error() {
		var map = new MapField<string, string> {
			{ Constants.Metadata.Type, _eventType },
		};

		var ex = Assert.Throws<RpcException>(() => MetadataHelpers.ParseGrpcMetadata(map));
		Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
	}
}
