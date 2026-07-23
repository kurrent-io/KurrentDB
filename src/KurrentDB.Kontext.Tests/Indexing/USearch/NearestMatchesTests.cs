// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Tests.Indexing.USearch;

public class NearestMatchesTests {
	[Test]
	public async Task Offer_Keeps_Smallest_Distance_Per_Id() {
		var matches = new NearestMatches();
		matches.Offer(1, 0.5f);
		matches.Offer(2, 0.9f);
		matches.Offer(1, 0.1f); // smaller — wins
		matches.Offer(1, 0.8f); // larger — ignored

		// id 1 now ranks ahead of id 2 because its kept distance (0.1) beats 2's (0.9).
		var nearest = matches.Nearest(10);
		await Assert.That(nearest.SequenceEqual([1ul, 2ul])).IsTrue();
	}

	[Test]
	public async Task Nearest_Sorts_By_Ascending_Distance() {
		var matches = new NearestMatches();
		matches.Offer(10, 0.9f);
		matches.Offer(20, 0.1f);
		matches.Offer(30, 0.5f);

		await Assert.That(matches.Nearest(10).SequenceEqual([20ul, 30ul, 10ul])).IsTrue();
	}

	[Test]
	public async Task Nearest_Respects_Limit() {
		var matches = new NearestMatches();
		for (ulong i = 0; i < 10; i++)
			matches.Offer(i, i); // distance == id

		await Assert.That(matches.Nearest(3).SequenceEqual([0ul, 1ul, 2ul])).IsTrue();
	}

	[Test]
	public async Task Nearest_Empty_Returns_Empty() {
		await Assert.That(new NearestMatches().Nearest(5)).IsEmpty();
	}

	[Test]
	public async Task Nearest_Limit_Above_Count_Returns_All() {
		var matches = new NearestMatches();
		matches.Offer(1, 0.1f);
		matches.Offer(2, 0.2f);
		await Assert.That(matches.Nearest(100)).Count().IsEqualTo(2);
	}
}
