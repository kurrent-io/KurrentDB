// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Google.Protobuf.Collections;
using KurrentDB.Core.Data;
using KurrentDB.TransactionLog.LogRecordSerialization.Proto;

namespace KurrentDB.Core.Services.Transport.Grpc;

public static class MetadataHelpers {
	public static void AddGrpcMetadataFrom(this MapField<string, string> self, EventRecord eventRecord) {
		self.Add(Constants.Metadata.Type, eventRecord.EventType);
		self.Add(Constants.Metadata.Created, eventRecord.TimeStamp.ToTicksSinceEpoch().ToString());

		if (eventRecord.Properties.Length > 0) {
			var properties = Properties.Parser.ParseFrom(eventRecord.Properties.Span);
			if (properties.PropertiesValues.TryGetValue(Constants.Metadata.SchemaVersionId, out var schemaVersionId) &&
			    properties.PropertiesValues.TryGetValue(Constants.Metadata.ContentType, out var propertyContentType)) {
				self.Add(Constants.Metadata.ContentType, propertyContentType.BytesValue.ToStringUtf8());
				self.Add(Constants.Metadata.SchemaVersionId, schemaVersionId.BytesValue.ToStringUtf8());
			}
			if (properties.PropertiesValues.TryGetValue(Constants.Metadata.MetadataContentType,
				    out var metadataContentType) &&
			    properties.PropertiesValues.TryGetValue(Constants.Metadata.MetadataSchemaVersionId,
				    out var metadataSchemaId)) {
				self.Add(Constants.Metadata.MetadataContentType, metadataContentType.BytesValue.ToStringUtf8());
				self.Add(Constants.Metadata.MetadataSchemaVersionId, metadataSchemaId.BytesValue.ToStringUtf8());
			}
		} else {
			self.Add(Constants.Metadata.ContentType,
				eventRecord.IsJson
					? Constants.Metadata.ContentTypes.ApplicationJson
					: Constants.Metadata.ContentTypes.ApplicationOctetStream);
		}
	}

	public static (string contentType, byte[] properties) ParseGrpcMetadata(MapField<string, string> metadata) {
		if (!metadata.TryGetValue(Constants.Metadata.ContentType, out var contentType)) {
			throw RpcExceptions.RequiredMetadataPropertyMissing(Constants.Metadata.ContentType);
		}

		var properties = new Properties();
		if (metadata.TryGetValue(Constants.Metadata.SchemaVersionId, out var schemaVersion)) {
			properties.PropertiesValues.Add(Constants.Metadata.ContentType,
				new PropertyValue { BytesValue = ByteString.CopyFromUtf8(contentType) });
			properties.PropertiesValues.Add(Constants.Metadata.SchemaVersionId,
				new PropertyValue { BytesValue = ByteString.CopyFromUtf8(schemaVersion) });
		}

		if (metadata.TryGetValue(Constants.Metadata.MetadataSchemaVersionId, out var metadataSchemaVersion) &&
		    metadata.TryGetValue(Constants.Metadata.MetadataContentType, out var metadataContentType)) {
			properties.PropertiesValues.Add(Constants.Metadata.MetadataContentType,
				new PropertyValue { BytesValue = ByteString.CopyFromUtf8(metadataContentType) });
			properties.PropertiesValues.Add(Constants.Metadata.MetadataSchemaVersionId,
				new PropertyValue { BytesValue = ByteString.CopyFromUtf8(metadataSchemaVersion) });
		}

		return (contentType, properties.ToByteArray());
	}
}
