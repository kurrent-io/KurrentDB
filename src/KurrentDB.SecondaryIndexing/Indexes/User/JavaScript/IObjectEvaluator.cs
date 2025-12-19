// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Indexes.User.JavaScript;

/// <summary>
/// Defines expression evaluation against a record type.
/// </summary>
/// <typeparam name="TRecord">The type of record to evaluate against</typeparam>
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
	object? Select(TRecord record, string selectorExpression);
}
