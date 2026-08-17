// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Fakes;

sealed class RescoringStage(Func<ScoredMemory, double> rescore) : IStep<Pool<NativeScale>, Pool<NativeScale>> {
	public ValueTask<Pool<NativeScale>> Execute(Pool<NativeScale> input, CancellationToken ct) =>
		new(input with { Memories = [.. input.Memories.Select(scored => scored with { Score = rescore(scored) })] });
}
