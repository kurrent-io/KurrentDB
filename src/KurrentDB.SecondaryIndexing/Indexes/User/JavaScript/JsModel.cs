// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers
// ReSharper disable MemberCanBePrivate.Global

using System.Text;
using Jint;
using Jint.Native;
using Jint.Native.Json;
using KurrentDB.Core.Data;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.SecondaryIndexing.Indexes.User.JavaScript;

public static class JsonParserExtensions {
	public static JsValue Decode(this JsonParser parser, ReadOnlySpan<byte> data, Func<bool>? canDecode = null) =>
		!data.IsEmpty && (canDecode?.Invoke() ?? true)
			? parser.Parse(Encoding.UTF8.GetString(data))
			: JsValue.Null;
}

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

	public JsValue? Value      { get => field ??= DecodeValue(); set; }
	public JsValue? Properties { get => field ??= DecodeProperties(); set; }

	public bool HasValue => Value is not null && !Value.IsNull();

	Func<JsValue> DecodeValue { get; set; }     = null!;
	Func<JsValue> DecodeProperties { get; set; } = null!;

	public void Remap(ResolvedEvent re, ulong sequence, JsonParser parser) {
		Id         = $"{re.OriginalEvent.EventId}";
		Sequence   = sequence;
		Redacted   = re.OriginalEvent.Flags.HasFlag(PrepareFlags.IsRedacted);
		Timestamp  = re.OriginalEvent.TimeStamp;

		DecodeValue      = () => parser.Decode(re.OriginalEvent.Data.Span, () => Schema.Format == JsSchemaFormat.Json && !Redacted);
		DecodeProperties = () => parser.Decode(re.OriginalEvent.Metadata.Span);

		Position.LogPosition    = re.OriginalEvent.LogPosition;
		Position.Stream         = re.OriginalEvent.EventStreamId;
		Position.StreamRevision = re.OriginalEvent.EventNumber;

		Schema.Name   = re.OriginalEvent.EventType;
		Schema.Format = Enum.Parse<JsSchemaFormat>(re.OriginalEvent.SchemaFormat);

		Value      = null;
		Properties = null;
	}
}
