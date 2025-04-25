// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using Google.Protobuf;
using Google.Protobuf.Collections;
using KurrentDB.Core.Data;
using KurrentDB.TransactionLog.LogRecordSerialization.Proto;

namespace KurrentDB.Core.Services.Transport.Grpc;

public static class MetadataHelpers {
	public static Dictionary<string, string> GetGrpcMetadata(EventRecord eventRecord) {
		var result = new Dictionary<string, string> {
			{ Constants.Metadata.Type, eventRecord.EventType },
			{ Constants.Metadata.Created, eventRecord.TimeStamp.ToTicksSinceEpoch().ToString() }
		};
		if (eventRecord.Properties.Length > 0) {
			var properties = Properties.Parser.ParseFrom(eventRecord.Properties.Span);
			if (properties.PropertiesValues.TryGetValue(Constants.Metadata.SchemaVersionId, out var schemaVersionId) &&
			    properties.PropertiesValues.TryGetValue(Constants.Metadata.ContentType, out var propertyContentType)) {
				result.Add(Constants.Metadata.ContentType, propertyContentType.BytesValue.ToStringUtf8());
				result.Add(Constants.Metadata.SchemaVersionId, schemaVersionId.BytesValue.ToStringUtf8());
			}
			if (properties.PropertiesValues.TryGetValue(Constants.Metadata.MetadataContentType,
				    out var metadataContentType) &&
			    properties.PropertiesValues.TryGetValue(Constants.Metadata.MetadataSchemaVersionId,
				    out var metadataSchemaId)) {
				result.Add(Constants.Metadata.MetadataContentType, metadataContentType.BytesValue.ToStringUtf8());
				result.Add(Constants.Metadata.MetadataSchemaVersionId, metadataSchemaId.BytesValue.ToStringUtf8());
			}
		} else {
			result.Add(Constants.Metadata.ContentType,
				eventRecord.IsJson
					? Constants.Metadata.ContentTypes.ApplicationJson
					: Constants.Metadata.ContentTypes.ApplicationOctetStream);
		}
		return result;
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
