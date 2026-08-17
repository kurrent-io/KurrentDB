// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Stages;

[Category("Stages")]
public class SeatAllocatorTests {
	[Test]
	public async ValueTask a_capped_kind_stops_at_its_share_of_the_seats() {
		IReadOnlyList<ScoredMemory> pool = [
			Chat("o1", 0.99), Chat("o2", 0.98), Chat("o3", 0.97), Chat("o4", 0.96), Chat("o5", 0.95),
			Fact("f1", 0.5), Fact("f2", 0.4),
		];

		// 0.35 · 10 = 3.5 seats, floored to 3 — a partial seat is not a seat
		var result = await Seated(Caps((Contracts.MemoryType.Observation, 0.35)), 10, pool);

		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["o1", "o2", "o3", "f1", "f2"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask a_share_too_small_to_buy_one_seat_seats_the_kind_out() {
		IReadOnlyList<ScoredMemory> pool = [Chat("o1", 0.99), Fact("f1", 0.5), Fact("f2", 0.4)];

		// 0.4 · 2 = 0.8 seats, floored to none: a kind priced out of its first seat gets none
		var result = await Seated(Caps((Contracts.MemoryType.Observation, 0.4)), 2, pool);

		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["f1", "f2"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask spare_seats_go_to_uncapped_kinds_in_running_order() {
		IReadOnlyList<ScoredMemory> pool = [Chat("o1", 0.99), Chat("o2", 0.98), Chat("o3", 0.97), Fact("f1", 0.5), Gossip("h1", 0.4)];

		// chat takes its 2 of 4 seats; the 2 it cannot hold are filled by whoever is next in line
		var result = await Seated(Caps((Contracts.MemoryType.Observation, 0.5)), 4, pool);

		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["o1", "o2", "f1", "h1"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask a_capped_kind_never_re_enters_through_leftovers() {
		IReadOnlyList<ScoredMemory> pool = [Chat("o1", 0.99), Chat("o2", 0.98), Chat("o3", 0.97), Chat("o4", 0.96)];

		var result = await Seated(Caps((Contracts.MemoryType.Observation, 0.5)), 4, pool);

		// 4 seats asked for, 4 candidates on hand, 2 come back: with nobody else to seat the answer
		// stays short rather than handing the ceiling back to the kind it was built against
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["o1", "o2"], CollectionOrdering.Matching);
		await Assert.That(result.Count).IsEqualTo(2);
	}

	[Test]
	public async ValueTask within_kind_order_is_the_running_order_and_kinds_are_never_re_sorted() {
		// The order MMR leaves behind, not a score sort: chat's best score sits last in the pool.
		IReadOnlyList<ScoredMemory> pool = [Fact("f1", 0.2), Chat("o1", 0.9), Fact("f2", 0.8), Chat("o2", 0.7), Chat("o3", 0.95), Fact("f3", 0.1)];

		var result = await Seated(Caps((Contracts.MemoryType.Observation, 0.2)), 10, pool);

		// o3 loses its seat to o2 on running order alone despite outscoring it, and no candidate
		// moves past another: f1's 0.2 still leads f2's 0.8
		await Assert.That(Fixtures.Ids(result)).IsEquivalentTo(["f1", "o1", "f2", "o2", "f3"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask no_caps_configured_cuts_exactly_as_the_plain_cut_does() {
		IReadOnlyList<ScoredMemory> pool = [Fact("f1", 0.2), Chat("o1", 0.9), Fact("f2", 0.8), Chat("o2", 0.7), Gossip("h1", 0.1)];

		var seated = await Seated(new SeatAllocationOptions(), 3, pool);
		var plain  = await Cut(3, pool);

		await Assert.That(Fixtures.Ids(seated)).IsEquivalentTo(Fixtures.Ids(plain), CollectionOrdering.Matching);
		await Assert.That(seated.Select(scored => scored.Score).ToList())
			.IsEquivalentTo(plain.Select(scored => scored.Score).ToList(), CollectionOrdering.Matching);
		await Assert.That(Fixtures.Ids(seated)).IsEquivalentTo(["f1", "o1", "f2"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask a_cap_on_a_kind_the_pool_never_had_is_a_no_op() {
		IReadOnlyList<ScoredMemory> pool = [Chat("o1", 0.9), Fact("f1", 0.8), Gossip("h1", 0.7)];

		var seated = await Seated(Caps((Contracts.MemoryType.Summary, 0.1)), 3, pool);
		var plain  = await Cut(3, pool);

		await Assert.That(Fixtures.Ids(seated)).IsEquivalentTo(Fixtures.Ids(plain), CollectionOrdering.Matching);
		await Assert.That(Fixtures.Ids(seated)).IsEquivalentTo(["o1", "f1", "h1"], CollectionOrdering.Matching);
	}

	static ScoredMemory Chat(string id, double score) =>
		Fixtures.Scored(id, score, type: Contracts.MemoryType.Observation);

	static ScoredMemory Fact(string id, double score) =>
		Fixtures.Scored(id, score, type: Contracts.MemoryType.Fact);

	static ScoredMemory Gossip(string id, double score) =>
		Fixtures.Scored(id, score, type: Contracts.MemoryType.Hearsay);

	static SeatAllocationOptions Caps(params (Contracts.MemoryType Kind, double Share)[] shares) =>
		new() { MaxShares = shares.ToDictionary(cap => cap.Kind, cap => cap.Share) };

	// The seats are only visible through the cut that takes them, so the stage is measured where it
	// ships: immediately in front of CutStep.
	static async ValueTask<IReadOnlyList<ScoredMemory>> Seated(SeatAllocationOptions caps, int limit, IReadOnlyList<ScoredMemory> pool) =>
		await SeatAllocator<NativeScale>.Create(caps)
			.Then(new CutStep<NativeScale>())
			.Execute(Carrier(limit, pool), CancellationToken.None);

	static async ValueTask<IReadOnlyList<ScoredMemory>> Cut(int limit, IReadOnlyList<ScoredMemory> pool) =>
		await new CutStep<NativeScale>().Execute(Carrier(limit, pool), CancellationToken.None);

	static Pool<NativeScale> Carrier(int limit, IReadOnlyList<ScoredMemory> pool) {
		var plan = Fixtures.Query(limit: limit);

		return new(new(new() { Text = plan.Text, Limit = plan.Limit }, plan), pool);
	}
}
