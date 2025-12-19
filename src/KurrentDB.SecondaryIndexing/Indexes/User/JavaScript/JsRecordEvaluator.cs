// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable InconsistentNaming
// ReSharper disable ArrangeTypeMemberModifiers

using Jint;
using Jint.Native;

namespace KurrentDB.SecondaryIndexing.Indexes.User.JavaScript;

public interface IObjectEvaluator<in TRecord> {
	/// <summary>
	/// Evaluates a predicate expression against the record.
	/// </summary>
	/// <param name="record">The record to evaluate against</param>
	/// <param name="predicateExpression">Expression that returns a boolean</param>
	/// <returns>True if the predicate passes, false otherwise</returns>
	bool Match(TRecord record, string predicateExpression);

	/// <summary>
	/// Evaluates a selector expression against the record.
	/// </summary>
	/// <param name="record">The record to evaluate against</param>
	/// <param name="selectorExpression">Expression that selects a value</param>
	/// <returns>The selected value</returns>
	JsValue Select(TRecord record, string selectorExpression);
}

public class JsRecordEvaluator(Engine engine) : IObjectEvaluator<JsRecord>, IDisposable {
	readonly Dictionary<string, JsValue> CompiledFunctions = new();

	public bool Match(JsRecord record, string predicateExpression) {
		var jsRecordValue = JsValue.FromObject(engine, record);
		var function = GetOrCompile(predicateExpression);

		return function.Call(jsRecordValue).AsBoolean();
	}

	public JsValue Select(JsRecord record, string selectorExpression) {
		var jsRecordValue = JsValue.FromObject(engine, record);
		var function = GetOrCompile(selectorExpression);

		return function.Call(jsRecordValue);
	}

	JsValue GetOrCompile(string expression) {
		if (CompiledFunctions.TryGetValue(expression, out var function)) return function;

		function = engine.Evaluate($"({expression})").AsFunctionInstance();
		CompiledFunctions[expression] = function;

		return function;
	}

	public void Dispose() {
		CompiledFunctions.Clear();
		GC.SuppressFinalize(this);
	}
}

