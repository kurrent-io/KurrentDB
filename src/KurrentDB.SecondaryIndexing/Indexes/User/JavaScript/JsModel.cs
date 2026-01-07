// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers
// ReSharper disable MemberCanBePrivate.Global

using System.Text.Json;
using System.Text.Json.Nodes;
using KurrentDB.Core.Data;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.SecondaryIndexing.Indexes.User.JavaScript;

public enum JsSchemaFormat {
	Undefined = 0,
	Json = 1,
	Protobuf = 2,
	Avro = 3,
	Bytes = 4
}

public class JsSchemaInfo {
	public string         Name   { get; set; } = "";
	public JsSchemaFormat Format { get; set; } = JsSchemaFormat.Undefined;
	public string?        Id     { get; set; }
}

public class JsRecordPosition {
	public string Stream         { get; set; } = "";
	public long   StreamRevision { get; set; } = -1;
	public long   LogPosition    { get; set; } = -1;
}

public class JsRecord {
	public string           Id        { get; set; } = "";
	public ulong            Sequence  { get; set; }
	public bool             Redacted  { get; set; }
	public DateTime         Timestamp { get; set; }
	public JsSchemaInfo     Schema    { get; set; } = new();
	public JsRecordPosition Position  { get; set; } = new();

	public JsonNode? Value      { get => field ??= DeserializeValue(); set; }
	public JsonNode? Properties { get => field ??= DeserializeProps(); set; }

	public bool HasValue => Value is not null;

	Func<JsonNode?> DeserializeValue { get; set; } = null!;
	Func<JsonNode?> DeserializeProps { get; set; } = null!;

	internal void Remap(EventRecord evt, ulong sequence, JsonSerializerOptions options) {
		Id         = $"{evt.EventId}";
		Sequence   = sequence;
		Redacted   = evt.Flags.HasFlag(PrepareFlags.IsRedacted);
		Timestamp  = evt.TimeStamp;

		Schema.Name   = evt.EventType;
		Schema.Format = Enum.Parse<JsSchemaFormat>(evt.SchemaFormat);

		DeserializeValue = !evt.Data.IsEmpty && !Redacted && Schema.Format == JsSchemaFormat.Json
			? () => JsonSerializer.Deserialize<JsonNode>(evt.Data.Span, options)
			: static () => null;

		DeserializeProps = !evt.Metadata.IsEmpty
			? () => JsonSerializer.Deserialize<JsonNode>(evt.Metadata.Span, options)
			: static () => null;

		Position.LogPosition    = evt.LogPosition;
		Position.Stream         = evt.EventStreamId;
		Position.StreamRevision = evt.EventNumber;

		Value      = null;
		Properties = null;
	}
}
