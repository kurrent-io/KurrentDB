// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Fusion;

[Category("Fusion")]
public class AdditiveNormalizedFuserTests {
	[Test]
	public async ValueTask divides_by_active_signals_only() {
		var fuser = AdditiveNormalizedFuser.Create(options => {
			options.Midpoint  = 5.0;
			options.Steepness = 0.7;
		});

		var vectorOnly = fuser.Fuse([new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("a", 0.8)])], Fixtures.Query());

		await Assert.That(vectorOnly[0].Score).IsEqualTo(0.8).Within(1e-12);

		// hybrid: (0.8 + sigmoid(5 at midpoint 5) = 0.5) / 2 active signals
		var hybrid = fuser.Fuse([
			new CandidateSet(RetrievalSources.Vector, [Fixtures.Candidate("a", 0.8)]),
			new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("a", 5.0)]),
		], Fixtures.Query());

		await Assert.That(hybrid[0].Score).IsEqualTo(0.65).Within(1e-12);
	}

	[Test]
	public async ValueTask sigmoid_adapts_to_term_count() {
		var fuser = AdditiveNormalizedFuser.Create();

		IReadOnlyList<CandidateSet> sets = [new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("k", 5.0)])];

		// two terms → rung (midpoint 5.0, steepness 0.7): sigmoid(5, 5, 0.7) = 0.5
		var shortQuery = fuser.Fuse(sets, Fixtures.Query("short query"));

		await Assert.That(shortQuery[0].Score).IsEqualTo(0.5).Within(1e-12);

		// seven terms → rung (midpoint 9.0, steepness 0.5): sigmoid(5, 9, 0.5) = 1 / (1 + e^2)
		var longQuery = fuser.Fuse(sets, Fixtures.Query("one two three four five six seven"));

		await Assert.That(longQuery[0].Score).IsEqualTo(1.0 / (1.0 + Math.Exp(2))).Within(1e-12);
	}

	[Test]
	public async ValueTask throws_on_unknown_source() {
		IReadOnlyList<CandidateSet> sets = [new CandidateSet(RetrievalSources.Hybrid, [Fixtures.Candidate("h", 0.9)])];

		await Assert.That(() => AdditiveNormalizedFuser.Create().Fuse(sets, Fixtures.Query())).Throws<NotSupportedException>();
	}
}
