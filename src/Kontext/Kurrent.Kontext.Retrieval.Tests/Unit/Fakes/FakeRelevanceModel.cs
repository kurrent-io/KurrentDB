// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Fakes;

sealed class FakeRelevanceModel : IRelevanceModel {
	readonly Func<IReadOnlyList<string>, IReadOnlyList<double>> score;

	public FakeRelevanceModel(params double[] scores) =>
		score = _ => scores;

	public FakeRelevanceModel(IReadOnlyDictionary<string, double> byContent) =>
		score = passages => passages.Select(passage => byContent[passage]).ToList();

	public FakeRelevanceModel(Func<string, double> byContent) =>
		score = passages => passages.Select(byContent).ToList();

	public int Calls { get; private set; }

	public string? LastQuery { get; private set; }

	public IReadOnlyList<string> LastPassages { get; private set; } = [];

	public ValueTask<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken ct = default) {
		Calls++;
		LastQuery    = query;
		LastPassages = passages.ToList();

		return ValueTask.FromResult(score(passages));
	}
}
