// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable InconsistentNaming
// ReSharper disable ArrangeTypeMemberModifiers

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using KurrentDB.Core.Data;

namespace KurrentDB.Scripting;

public class JsRecordEvaluator {
	readonly JsRecord _record;
	readonly JsValue _jsValue;

	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(JsRecord))]
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(JsSchemaInfo))]
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(JsRecordPosition))]
	public JsRecordEvaluator(Engine engine) {
		_record = new();
		_jsValue = JsValue.FromObjectWithType(engine, _record, typeof(JsRecord));
	}

	public void MapRecord(ResolvedEvent re, ulong sequence) =>
		MapRecord(re.OriginalEvent, sequence);

	public void MapRecord(EventRecord record, ulong sequence) =>
		_record.Remap(record, sequence);

	public bool Match(Function? filter) =>
		filter?.Call(_jsValue).AsBoolean() ?? true;

	public JsValue? Select(Function? selector) =>
		selector?.Call(_jsValue) ?? null;

	public static Function? Compile(Engine engine, string? expression) =>
		string.IsNullOrEmpty(expression) ? null : engine.Evaluate($"({expression})").AsFunctionInstance();
}

