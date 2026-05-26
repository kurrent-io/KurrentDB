// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Tests.Search;

public class SearchAlgorithmsRrfTests {
	[Test]
	public async Task AccumulateRrfScores_Single_List() {
		var scores = new Dictionary<ulong, float>();

		SearchAlgorithms.AccumulateRrfScores([1, 2], scores);

		// rank 1: 1/(60+1), rank 2: 1/(60+2)
		await Assert.That(scores[1]).IsEqualTo(1.0f / 61);
		await Assert.That(scores[2]).IsEqualTo(1.0f / 62);
	}

	[Test]
	public async Task AccumulateRrfScores_Two_Lists_Accumulates() {
		var scores = new Dictionary<ulong, float>();

		SearchAlgorithms.AccumulateRrfScores([1, 2], scores);
		SearchAlgorithms.AccumulateRrfScores([2, 1], scores);

		var expected = 1.0f / 61 + 1.0f / 62;
		await Assert.That(MathF.Abs(scores[1] - expected)).IsLessThan(1e-6f);
		await Assert.That(MathF.Abs(scores[2] - expected)).IsLessThan(1e-6f);
	}

	[Test]
	public async Task NormalizeRrfScores_Max_Possible_Score_Is_1() {
		// A doc ranked #1 in both lists gets 2/(k+1) which normalizes to 1.0
		var scores = new Dictionary<ulong, float> {
			[1] = 2.0f / (SearchAlgorithms.RrfK + 1),
		};

		var normalized = SearchAlgorithms.NormalizeRrfScores(scores);
		await Assert.That(normalized[0].Score).IsEqualTo(1.0f);
	}

	[Test]
	public async Task NormalizeRrfScores_Sorted_Descending() {
		var scores = new Dictionary<ulong, float> {
			[1] = 0.01f,  // low
			[2] = 0.03f,  // high
			[3] = 0.02f,  // mid
		};

		var normalized = SearchAlgorithms.NormalizeRrfScores(scores);
		await Assert.That(normalized[0].DocId).IsEqualTo(2ul);
		await Assert.That(normalized[1].DocId).IsEqualTo(3ul);
		await Assert.That(normalized[2].DocId).IsEqualTo(1ul);
	}
}