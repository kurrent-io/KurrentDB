// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable InconsistentNaming
// ReSharper disable ArrangeTypeMemberModifiers

using Jint;
using Jint.Native;

namespace KurrentDB.SecondaryIndexing.Indexes.User.JavaScript;

public class JsRecordEvaluator(Engine engine, object skip) : IObjectEvaluator<JsRecord> {
	readonly Dictionary<string, JsValue> CompiledFunctions = new();

	public bool Match(JsRecord record, string predicateExpression) {
		var jsRecordValue = JsValue.FromObject(engine, record);
		var function = GetOrCompile(predicateExpression);

		return function.Call(jsRecordValue).AsBoolean();
	}

	public object? Select(JsRecord record, string selectorExpression) {
		var jsRecordValue = JsValue.FromObject(engine, record);
		var function = GetOrCompile(selectorExpression);

		return function.Call(jsRecordValue);
	}

	public bool IsSkip(JsValue value) => skip.Equals(value);

	JsValue GetOrCompile(string expression) {
		if (CompiledFunctions.TryGetValue(expression, out var function)) return function;

		function = engine.Evaluate($"({expression})");
		CompiledFunctions[expression] = function;

		return function;
	}
}
