// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Google.Protobuf.Collections;
using KurrentDB.Core.Data;
using KurrentDB.Core.LogRecordSerialization;

namespace KurrentDB.Core.Services.Transport.Grpc;

public static class MetadataHelpers {
	public static void AddGrpcMetadataFrom(this MapField<string, string> self, EventRecord eventRecord) {
		self.Add(Constants.Metadata.Type, eventRecord.EventType);
		self.Add(Constants.Metadata.Created, eventRecord.TimeStamp.ToTicksSinceEpoch().ToString());
		self.Add(Constants.Metadata.ContentType,
				eventRecord.IsJson
					? Constants.Metadata.ContentTypes.ApplicationJson
					: Constants.Metadata.ContentTypes.ApplicationOctetStream);

		if (eventRecord.Properties.Length == 0)
			return;

		var properties = Properties.Parser.ParseFrom(eventRecord.Properties.Span);
		foreach (var (key, value) in properties.PropertiesValues) {
			if (!value.HasBytesValue) continue;
			self[key] = value.BytesValue.ToStringUtf8();
		}
	}

	public static (bool isJson, string eventType, byte[] properties) ParseGrpcMetadata(MapField<string, string> metadata) {
		if (!metadata.TryGetValue(Constants.Metadata.Type, out var eventType)) {
			throw RpcExceptions.RequiredMetadataPropertyMissing(Constants.Metadata.Type);
		}

		if (!metadata.TryGetValue(Constants.Metadata.ContentType, out var contentType)) {
			throw RpcExceptions.RequiredMetadataPropertyMissing(Constants.Metadata.ContentType);
		}

		var isJson = contentType == Constants.Metadata.ContentTypes.ApplicationJson;

		var properties = new Properties();
		foreach (var (key, value) in metadata) {
			if (key == Constants.Metadata.ContentType && value
				    is Constants.Metadata.ContentTypes.ApplicationJson
				    or Constants.Metadata.ContentTypes.ApplicationOctetStream) {
				continue;
			}
			properties.PropertiesValues.Add(key,
				new DynamicValue { BytesValue = ByteString.CopyFromUtf8(value) });
		}

		return (isJson, eventType, properties.ToByteArray());
	}
}
