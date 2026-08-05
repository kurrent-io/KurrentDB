// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Fakes;

sealed class ThrowingSearch(string name) : ISearch {
	public string Name => name;

	public ValueTask<CandidateSet> SearchAsync(PlannedQuery query, CancellationToken ct = default) =>
		throw new InvalidOperationException($"The '{name}' leg is down.");
}
