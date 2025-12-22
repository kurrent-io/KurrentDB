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
	public string           Id         { get; set; } = "";
	public ulong            Sequence   { get; set; }
	public bool             Redacted   { get; set; }
	public DateTime         Timestamp  { get; set; }
	public JsSchemaInfo     Schema     { get; set; } = new();
	public JsRecordPosition Position   { get; set; } = new();

	public JsonNode? Value      { get => field ??= DecodeValue(); set; }
	public JsonNode? Properties { get => field ??= DecodeProperties(); set; }

	public bool HasValue => Value is not null;

	Func<JsonNode?> DecodeValue { get; set; }      = null!;
	Func<JsonNode?> DecodeProperties { get; set; } = null!;

	public void Remap(ResolvedEvent re, ulong sequence, JsonSerializerOptions options) {
		Id         = $"{re.OriginalEvent.EventId}";
		Sequence   = sequence;
		Redacted   = re.OriginalEvent.Flags.HasFlag(PrepareFlags.IsRedacted);
		Timestamp  = re.OriginalEvent.TimeStamp;

		Schema.Name   = re.OriginalEvent.EventType;
		Schema.Format = Enum.Parse<JsSchemaFormat>(re.OriginalEvent.SchemaFormat);

		DecodeValue = () => Schema.Format == JsSchemaFormat.Json && !Redacted && !re.OriginalEvent.Data.IsEmpty
			? JsonSerializer.Deserialize<JsonNode>(re.OriginalEvent.Data.Span, options)
			: null;
		DecodeProperties = () => !re.OriginalEvent.Metadata.IsEmpty
			? JsonSerializer.Deserialize<JsonNode>(re.OriginalEvent.Metadata.Span, options)
			: null;

		Position.LogPosition    = re.OriginalEvent.LogPosition;
		Position.Stream         = re.OriginalEvent.EventStreamId;
		Position.StreamRevision = re.OriginalEvent.EventNumber;


		Value      = null;
		Properties = null;
	}
}
