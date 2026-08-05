// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Fusion;

[Category("Fusion")]
public class AdditiveFusionOptionsTests {
	[Test]
	public async ValueTask empty_rungs_throws_invalid_operation_exception() {
		var fuser = AdditiveNormalizedFuser.Create(options => options.Rungs = []);

		IReadOnlyList<CandidateSet> sets = [new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("k", 5.0)])];

		// SigmoidFor guards Rungs.Count == 0 before FirstOrDefault's fallback argument (Rungs[^1]) would
		// otherwise be evaluated eagerly and let List<T>'s indexer throw ArgumentOutOfRangeException by
		// accident. An empty ladder now fails deliberately, with a message naming the actual problem.
		await Assert.That(() => fuser.Fuse(sets, Fixtures.Query()))
			.Throws<InvalidOperationException>()
			.WithMessageContaining("Rungs is empty");
	}

	[Test]
	public async ValueTask unsorted_rungs_select_by_list_order_not_ascending_maxterms() {
		var fuser = AdditiveNormalizedFuser.Create(options => options.Rungs = [
			new(MaxTerms: 100, Midpoint: 1.0, Steepness: 1.0),
			new(MaxTerms: 3, Midpoint: 99.0, Steepness: 99.0),
		]);

		IReadOnlyList<CandidateSet> sets = [new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("k", 1.0)])];

		// FirstOrDefault takes the FIRST rung whose MaxTerms >= termCount by list position, not by
		// ascending MaxTerms. A two-term query is <= 100 as well as <= 3, but the 100-bucket is listed
		// first, so it wins over the tighter 3-bucket a sorted ladder would have picked. Pins that the
		// ladder's correctness depends entirely on caller-maintained ascending order.
		var pool = fuser.Fuse(sets, Fixtures.Query("two terms"));

		// sigmoid(1, midpoint 1, steepness 1) = 0.5 — proves the 100-bucket won, not the 3-bucket (which
		// would saturate sigmoid(1, 99, 99) to ~0)
		await Assert.That(pool[0].Score).IsEqualTo(0.5).Within(1e-12);
	}

	[Test]
	public async ValueTask pinning_only_midpoint_still_takes_steepness_from_the_selected_rung() {
		var fuser = AdditiveNormalizedFuser.Create(options => options.Midpoint = 50.0);

		IReadOnlyList<CandidateSet> sets = [new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("k", 51.0)])];

		// Midpoint alone fails the both-pinned check, so SigmoidFor falls through to rung selection.
		// Two terms lands on the default first rung (midpoint 5.0, steepness 0.7); Midpoint ?? rung.Midpoint
		// keeps the pinned 50.0, while Steepness ?? rung.Steepness takes the rung's 0.7.
		var pool = fuser.Fuse(sets, Fixtures.Query("two terms"));

		await Assert.That(pool[0].Score).IsEqualTo(1.0 / (1.0 + Math.Exp(-0.7))).Within(1e-12);
	}

	[Test]
	public async ValueTask pinning_only_steepness_still_takes_midpoint_from_the_selected_rung() {
		var fuser = AdditiveNormalizedFuser.Create(options => options.Steepness = 2.0);

		IReadOnlyList<CandidateSet> sets = [new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("k", 6.0)])];

		// Steepness alone falls through the same way: two terms selects the default first rung
		// (midpoint 5.0, steepness 0.7); Steepness ?? rung.Steepness keeps the pinned 2.0, while
		// Midpoint ?? rung.Midpoint takes the rung's 5.0.
		var pool = fuser.Fuse(sets, Fixtures.Query("two terms"));

		await Assert.That(pool[0].Score).IsEqualTo(1.0 / (1.0 + Math.Exp(-2.0))).Within(1e-12);
	}

	[Test]
	public async ValueTask pinning_both_ignores_rungs_entirely_even_a_nonsense_ladder() {
		var fuser = AdditiveNormalizedFuser.Create(options => {
			options.Midpoint  = 5.0;
			options.Steepness = 0.7;
			options.Rungs     = []; // would throw if SigmoidFor ever touched Rungs
		});

		IReadOnlyList<CandidateSet> sets = [new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("k", 5.0)])];

		// Both Midpoint and Steepness set short-circuits before Rungs is ever read, for a query long
		// enough that a real ladder lookup would land far from rung 1 — an empty (nonsense) Rungs would
		// throw if it were touched, so reaching a value at all proves it was skipped entirely.
		var pool = fuser.Fuse(sets, Fixtures.Query("this query has quite a few terms scattered across it"));

		await Assert.That(pool[0].Score).IsEqualTo(0.5).Within(1e-12);
	}

	[Test]
	[Arguments(3, 5.0, 0.7)]
	[Arguments(4, 7.0, 0.6)]
	[Arguments(6, 7.0, 0.6)]
	[Arguments(7, 9.0, 0.5)]
	[Arguments(9, 9.0, 0.5)]
	[Arguments(10, 10.0, 0.5)]
	[Arguments(15, 10.0, 0.5)]
	[Arguments(16, 12.0, 0.5)]
	[Arguments(200, 12.0, 0.5)]
	public async ValueTask sigmoid_rung_boundaries_select_by_term_count(int termCount, double expectedMidpoint, double expectedSteepness) {
		var fuser = AdditiveNormalizedFuser.Create();
		var query = string.Join(' ', Enumerable.Repeat("w", termCount));

		// Score set one unit above the expected rung's midpoint: sigmoid = 1 / (1 + e^-steepness), a
		// value that moves if EITHER the wrong rung is picked (wrong midpoint) or the rung's steepness is
		// wrong — catches an off-by-one on any of the 3/6/9/15 boundaries, not just a shape match.
		IReadOnlyList<CandidateSet> sets = [new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("k", expectedMidpoint + 1.0)])];

		var pool = fuser.Fuse(sets, Fixtures.Query(query));

		await Assert.That(pool[0].Score).IsEqualTo(1.0 / (1.0 + Math.Exp(-expectedSteepness))).Within(1e-12);
	}

	[Test]
	public async ValueTask term_counting_ignores_repeated_and_surrounding_whitespace() {
		var fuser = AdditiveNormalizedFuser.Create();

		IReadOnlyList<CandidateSet> sets = [new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("k", 8.0)])];

		// "  one   two  three   four  " has 4 real terms once RemoveEmptyEntries | TrimEntries collapses
		// the runs of spaces and the leading/trailing padding — same bucket (MaxTerms 6, midpoint 7.0,
		// steepness 0.6) as a cleanly single-spaced 4-term query would land on.
		var pool = fuser.Fuse(sets, Fixtures.Query("  one   two  three   four  "));

		await Assert.That(pool[0].Score).IsEqualTo(1.0 / (1.0 + Math.Exp(-0.6))).Within(1e-12);
	}

	[Test]
	public async ValueTask empty_query_counts_zero_terms_and_selects_the_first_rung() {
		var fuser = AdditiveNormalizedFuser.Create();

		IReadOnlyList<CandidateSet> sets = [new CandidateSet(RetrievalSources.Keyword, [Fixtures.Candidate("k", 6.0)])];

		// An empty query splits to zero terms; 0 <= 3 is true, so it lands on the first rung
		// (midpoint 5.0, steepness 0.7) — the same bucket a genuinely short query would hit.
		var pool = fuser.Fuse(sets, Fixtures.Query(""));

		await Assert.That(pool[0].Score).IsEqualTo(1.0 / (1.0 + Math.Exp(-0.7))).Within(1e-12);
	}
}
