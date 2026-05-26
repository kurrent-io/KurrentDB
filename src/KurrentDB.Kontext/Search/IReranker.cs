// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Search;

/// <summary>
/// Re-scores candidate documents against a query and yields them sorted by relevance.
/// </summary>
public interface IReranker {
	IAsyncEnumerable<ScoredDoc> RerankAsync(
		string query,
		IReadOnlyList<(ulong DocId, string Document)> candidates,
		CancellationToken cancellationToken = default);
}