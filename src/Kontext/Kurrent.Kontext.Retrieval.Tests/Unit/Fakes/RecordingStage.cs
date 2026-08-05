// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Fakes;

sealed class RecordingStage(string name, List<string> calls) : IRetrievalStage {
	public ValueTask<IReadOnlyList<ScoredMemory>> ProcessAsync(PlannedQuery query, IReadOnlyList<ScoredMemory> pool, CancellationToken ct = default) {
		calls.Add(name);
		return ValueTask.FromResult(pool);
	}
}
