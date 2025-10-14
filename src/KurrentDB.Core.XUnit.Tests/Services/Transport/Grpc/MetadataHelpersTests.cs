// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Google.Protobuf.Collections;
using Grpc.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Transport.Grpc;
using KurrentDB.Core.TransactionLog.LogRecords;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services.Transport.Grpc;

public class MetadataHelpersTests {
	private readonly DateTime _timeStamp = DateTime.Now;
	private readonly string _eventType = "test-event";
	private readonly string _streamName = "test-stream";

	private string TicksSinceEpoch => (_timeStamp - DateTime.UnixEpoch).Ticks.ToString();

	private EventRecord CreateEventRecord(string contentType) {
		var flags = contentType is Constants.Metadata.ContentTypes.ApplicationJson
			? PrepareFlags.IsJson | PrepareFlags.SingleWrite
			: PrepareFlags.SingleWrite;
		return new EventRecord(0, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0, _streamName, -1, _timeStamp,
			flags, _eventType, "{\"foo\":\"bar\"}"u8.ToArray(), "test-metadata"u8.ToArray());
	}

	[Theory]
	[InlineData(Constants.Metadata.ContentTypes.ApplicationOctetStream)]
	[InlineData(Constants.Metadata.ContentTypes.ApplicationJson)]
	public void when_mapping_event_record_should_use_is_json_for_content_type(string contentType) {
		var eventRecord = CreateEventRecord(contentType);

		var map = new MapField<string, string>();
		map.AddGrpcMetadataFrom(eventRecord);

		Assert.Equal(3, map.Count);
		Assert.Equal(contentType, map[Constants.Metadata.ContentType]);
		Assert.Equal(_eventType, map[Constants.Metadata.Type]);
		Assert.Equal(TicksSinceEpoch, map[Constants.Metadata.Created]);
	}

	[Theory]
	[InlineData(Constants.Metadata.ContentTypes.ApplicationOctetStream)]
	[InlineData(Constants.Metadata.ContentTypes.ApplicationJson)]
	public void when_parsing_metadata_with_json_or_octet_content_types(string contentType) {
		var map = new MapField<string, string> {
			{ Constants.Metadata.Type, _eventType },
			{ Constants.Metadata.ContentType, contentType}
		};

		var parsed = MetadataHelpers.ParseGrpcMetadata(map);
		Assert.Equal(contentType == Constants.Metadata.ContentTypes.ApplicationJson, parsed.isJson);
		Assert.Equal(_eventType, parsed.eventType);
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
