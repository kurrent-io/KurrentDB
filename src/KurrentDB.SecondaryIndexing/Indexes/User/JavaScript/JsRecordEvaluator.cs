// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable InconsistentNaming
// ReSharper disable ArrangeTypeMemberModifiers

using System.Runtime.CompilerServices;
using System.Text.Json;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using KurrentDB.Core.Data;

namespace KurrentDB.SecondaryIndexing.Indexes.User.JavaScript;

public class JsRecordEvaluator : IDisposable {
	readonly Engine _engine;
	readonly JsonSerializerOptions _serializerOptions;
	readonly Dictionary<string, Function> _compiledFunctions;

	readonly JsRecord _record;
	readonly JsValue _jsValue;

	public JsRecordEvaluator(Engine engine, JsonSerializerOptions serializerOptions) {
		_engine            = engine;
		_serializerOptions = serializerOptions;
		_compiledFunctions = new();

		_record  = new();
		_jsValue = JsValue.FromObjectWithType(engine, _record, typeof(JsRecord));
	}

	public void MapRecord(ResolvedEvent re, ulong sequence) =>
		_record.Remap(re.OriginalEvent, sequence, _serializerOptions);

	public bool Match(string? predicateExpression) =>
		string.IsNullOrEmpty(predicateExpression) || GetOrCompile(predicateExpression).Call(_jsValue).AsBoolean();

	public JsValue Select(string? selectorExpression) =>
		!string.IsNullOrEmpty(selectorExpression) ? GetOrCompile(selectorExpression).Call(_jsValue) : JsValue.Null;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	Function GetOrCompile(string expression) {
		if (_compiledFunctions.TryGetValue(expression, out var function)) return function;
		function = _engine.Evaluate($"({expression})").AsFunctionInstance();
		_compiledFunctions[expression] = function;
		return function;
	}

	public void Dispose() {
		_compiledFunctions.Clear();
		GC.SuppressFinalize(this);
	}
}

