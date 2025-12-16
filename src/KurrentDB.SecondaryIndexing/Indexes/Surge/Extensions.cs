// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Nodes;
using Kurrent.Surge;
using Kurrent.Surge.Schema;
using KurrentDB.Core.Data;

namespace KurrentDB.SecondaryIndexing.Indexes.Surge;

public static class Extensions {
	public static TFPos ToTFPos(this LogPosition position) {
		if (!position.PreparePosition.HasValue || !position.CommitPosition.HasValue)
			return TFPos.HeadOfTf;

		return new TFPos((long)position.CommitPosition, (long)position.PreparePosition);
	}

	public static JsEventStoreRecord MapToJsRecord(this SurgeRecord record, JsonSerializerOptions options) {
		var recordValue = !record.IsDecoded
			? JsonSerializer.Deserialize<JsonNode>(record.Data.Span, options)
			: record.Value as JsonNode ?? JsonSerializer.SerializeToNode(record.Value, options);

		return new() {
			RecordId = record.Id.ToString(),

			Position = new(
				record.StreamId,
				record.PartitionId,
				(ulong)record.LogPosition.CommitPosition!
			),

			SchemaInfo = new(
				record.SchemaInfo.SchemaName,
				record.SchemaInfo.SchemaDataFormat switch {
					SchemaDataFormat.Json     => JsSchemaDefinitionType.Json,
					SchemaDataFormat.Protobuf => JsSchemaDefinitionType.Protobuf,
					SchemaDataFormat.Avro     => JsSchemaDefinitionType.Avro,
					SchemaDataFormat.Bytes    => JsSchemaDefinitionType.Bytes,
					_                         => JsSchemaDefinitionType.Undefined,
				}
			),

			SequenceId = record.SequenceId,
			IsRedacted = record.IsRedacted,
			Headers    = record.Headers.WithoutSchemaInfo(),

			Value = recordValue
		};
	}
}
