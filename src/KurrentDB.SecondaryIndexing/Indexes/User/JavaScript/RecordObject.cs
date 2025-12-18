// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Jint;
using Jint.Native;
using Jint.Native.Json;
using KurrentDB.Core.Data;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.SecondaryIndexing.Indexes.User.JavaScript;

internal sealed class RecordObject : JsObject {
	private readonly PositionObject _position;
	private readonly SchemaInfoObject _schemaInfo;

	public RecordObject(Engine engine, JsonParser parser) : base(engine, parser) {
		_position = new PositionObject(engine, parser);
		SetReadOnlyProperty("position", _position);

		_schemaInfo = new SchemaInfoObject(engine, parser);
		SetReadOnlyProperty("schemaInfo", _schemaInfo);
	}

	public override JsValue Get(JsValue property, JsValue receiver) {
		return property.AsString() switch {
			"value" => Value,
			"headers" => Headers,
			_ => base.Get(property, receiver)
		};
	}

	private string RecordId {
		set => SetReadOnlyProperty("recordId", value);
	}

	private ulong SequenceId {
		set => SetReadOnlyProperty("sequenceId", value);
	}

	private bool IsRedacted {
		set => SetReadOnlyProperty("isRedacted", value);
		get => TryGetValue("isRedacted", out var isRedactedValue) && isRedactedValue.AsBoolean();
	}

	private JsValue Value {
		get => TryParseJson(Data, "value", () => IsJson && !IsRedacted);
		set => SetReadOnlyProperty("value", value);
	}

	private JsValue Headers {
		get => TryParseJson(Metadata, "headers");
		set => SetReadOnlyProperty("headers", value);
	}

	private ReadOnlyMemory<byte> Data { get; set; }
	private ReadOnlyMemory<byte> Metadata { get; set; }
	private bool IsJson { get; set; }

	protected override void EnsureProperties() {
		_ = Value;
		_ = Headers;
	}

	public void MapFrom(ResolvedEvent resolvedEvent, ulong sequenceId) {
		RecordId = $"{resolvedEvent.OriginalEvent.EventId}";
		SequenceId = sequenceId;
		IsRedacted = resolvedEvent.OriginalEvent.Flags.HasFlag(PrepareFlags.IsRedacted);
		IsJson = resolvedEvent.OriginalEvent.IsJson;
		Data = resolvedEvent.OriginalEvent.Data;
		Metadata = resolvedEvent.OriginalEvent.Metadata;
		Value = Undefined;
		Headers = Undefined;

		_position.MapFrom(resolvedEvent, sequenceId);
		_schemaInfo.MapFrom(resolvedEvent, sequenceId);
	}
}
