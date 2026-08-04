// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Fakes;

sealed class FakeSearch(string name, params SearchCandidate[] candidates) : ISearch {
	public string Name => name;

	public ValueTask<CandidateSet> SearchAsync(PlannedQuery query, CancellationToken ct = default) =>
		ValueTask.FromResult(new CandidateSet(name, candidates));
}
