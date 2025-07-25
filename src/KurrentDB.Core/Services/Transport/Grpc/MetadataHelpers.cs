// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.Collections;
using KurrentDB.Core.Data;

namespace KurrentDB.Core.Services.Transport.Grpc;

public static class MetadataHelpers {
	// Called on read to populate the grpc metadata from the persisted event record
	public static void AddGrpcMetadataFrom(this MapField<string, string> self, EventRecord eventRecord) {
		self.Add(Constants.Metadata.Type, eventRecord.EventType);
		self.Add(Constants.Metadata.Created, eventRecord.TimeStamp.ToTicksSinceEpoch().ToString());
		self.Add(Constants.Metadata.ContentType,
				eventRecord.IsJson
					? Constants.Metadata.ContentTypes.ApplicationJson
					: Constants.Metadata.ContentTypes.ApplicationOctetStream);
	}

	// Called on write to separate out information received via metadata for storage in the log records
	public static (bool isJson, string eventType) ParseGrpcMetadata(MapField<string, string> metadata) {
		if (!metadata.TryGetValue(Constants.Metadata.Type, out var eventType)) {
			throw RpcExceptions.RequiredMetadataPropertyMissing(Constants.Metadata.Type);
		}

		if (!metadata.TryGetValue(Constants.Metadata.ContentType, out var contentType)) {
			throw RpcExceptions.RequiredMetadataPropertyMissing(Constants.Metadata.ContentType);
		}

		var isJson = contentType == Constants.Metadata.ContentTypes.ApplicationJson;

		return (isJson, eventType);
	}
}
