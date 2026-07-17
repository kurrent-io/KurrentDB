// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq.Expressions;

namespace Kurrent.Kontext;

/// <summary>
/// Builds the LINQ predicates the vector store translates. Deliberately confined to the two shapes
/// every target connector supports — property equality and list containment, combined with
/// <c>&amp;&amp;</c> — so a filter that works against the in-memory store keeps working against a
/// DuckDB/Lance-backed one. <c>== false</c> is used instead of <c>!</c> for the same reason: it
/// keeps the tree a plain equality node, the only negation shape the portable surface has.
/// </summary>
static class MemoryRecordFilters {
	/// <summary>
	/// Recall's view: only what is currently true — never retracted, never superseded — carrying
	/// every requested tag.
	/// </summary>
	public static Expression<Func<MemoryRecord, bool>> Recall(IEnumerable<string> tags) =>
		WithTags(r => r.IsRetracted == false && r.IsSuperseded == false, tags);

	/// <summary>
	/// Recollect's view: retracted memories were mistakes and never come back; superseded ones stay
	/// readable history. Optionally narrowed to one memory type (the proto's any-of type set becomes
	/// one query per type, since the portable filter surface has no OR).
	/// </summary>
	public static Expression<Func<MemoryRecord, bool>> Recollect(IEnumerable<string> tags, int? memoryType) {
		var filter = WithTags(r => r.IsRetracted == false, tags);

		if (memoryType is { } type)
			filter = And(filter, r => r.MemoryType == type);

		return filter;
	}

	static Expression<Func<MemoryRecord, bool>> WithTags(Expression<Func<MemoryRecord, bool>> filter, IEnumerable<string> tags) {
		foreach (var tag in tags)
			filter = And(filter, r => r.Tags.Contains(tag));

		return filter;
	}

	// Splices two predicates into one lambda by rebinding the right side onto the left's parameter —
	// connectors translate a single expression tree and reject Expression.Invoke composition.
	static Expression<Func<MemoryRecord, bool>> And(Expression<Func<MemoryRecord, bool>> left, Expression<Func<MemoryRecord, bool>> right) {
		var body = new RebindParameter(right.Parameters[0], left.Parameters[0]).Visit(right.Body);
		return Expression.Lambda<Func<MemoryRecord, bool>>(Expression.AndAlso(left.Body, body), left.Parameters[0]);
	}

	sealed class RebindParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor {
		protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
	}
}
