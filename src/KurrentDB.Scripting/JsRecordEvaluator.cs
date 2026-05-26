// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable InconsistentNaming
// ReSharper disable ArrangeTypeMemberModifiers

using System.Text.Json;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using KurrentDB.Core.Data;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Scripting;

public class JsRecordEvaluator {
	readonly JsonSerializerOptions _serializerOptions;
	readonly JsRecord _record;
	readonly JsValue _jsValue;

	public JsRecordEvaluator(Engine engine, JsonSerializerOptions serializerOptions) {
		_serializerOptions = serializerOptions;
		_record  = new();
		_jsValue = JsValue.FromObjectWithType(engine, _record, typeof(JsRecord));
	}

	public void MapRecord(ResolvedEvent re, ulong sequence) =>
		MapRecord(re.OriginalEvent, sequence);

	public void MapRecord(EventRecord record, ulong sequence) =>
		_record.Remap(record, sequence, _serializerOptions);

	public bool Match(Function? filter) =>
		filter?.Call(_jsValue).AsBoolean() ?? true;

	public JsValue? Select(Function? selector) =>
		selector?.Call(_jsValue) ?? null;

	public static Function? Compile(Engine engine, string? expression) =>
		string.IsNullOrEmpty(expression) ? null : engine.Evaluate($"({expression})").AsFunctionInstance();
}

