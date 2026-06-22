// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using Jint;
using Jint.Native.Function;
using KurrentDB.Core.Data;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using KurrentDB.Scripting;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Kontext.Indexing;

public enum FilterResult {
	Match,      // a rule's prefix matched and its JS filter (if any) accepted the event
	Excluded,   // a rule's prefix matched but its JS filter rejected the event
	OutOfScope, // no rule's prefix matched
}

public sealed class WorkspaceFilter {
	static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	readonly record struct Rule(string StreamPrefix, Function? Filter);

	readonly Rule[] _rules;
	readonly JsRecordEvaluator? _evaluator;
	ulong _sequence;

	WorkspaceFilter(Rule[] rules, JsRecordEvaluator? evaluator) {
		_rules = rules;
		_evaluator = evaluator;
	}

	public static WorkspaceFilter Compile(IReadOnlyList<FilterRule> rules) {
		Engine? engine = null;
		JsRecordEvaluator? evaluator = null;
		foreach (var rule in rules)
			if (!string.IsNullOrWhiteSpace(rule.Filter)) {
				engine = JintEngineFactory.CreateEngine();
				evaluator = new JsRecordEvaluator(engine, JsonOptions);
				break;
			}

		var compiled = new Rule[rules.Count];
		for (var i = 0; i < rules.Count; i++)
			compiled[i] = new Rule(
				rules[i].StreamPrefix,
				string.IsNullOrWhiteSpace(rules[i].Filter) ? null : JsRecordEvaluator.Compile(engine!, rules[i].Filter));

		return new WorkspaceFilter(compiled, evaluator);
	}

	public FilterResult Evaluate(EventRecord record, ILogger logger) {
		if (_rules.Length == 0)
			return FilterResult.Match;

		foreach (var rule in _rules) {
			if (!record.EventStreamId.StartsWith(rule.StreamPrefix, StringComparison.Ordinal))
				continue;
			if (rule.Filter is null)
				return FilterResult.Match;

			_evaluator!.MapRecord(record, ++_sequence);
			try {
				return _evaluator.Match(rule.Filter) ? FilterResult.Match : FilterResult.Excluded;
			} catch (Exception ex) {
				logger.LogError(ex,
					"Workspace filter for prefix '{Prefix}' failed on stream '{Stream}'; excluding the event",
					rule.StreamPrefix, record.EventStreamId);
				return FilterResult.Excluded;
			}
		}

		return FilterResult.OutOfScope;
	}
}
