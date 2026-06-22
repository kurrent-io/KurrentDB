// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Tests.Indexing.USearch;

public class ActiveSnapshotTests {
	static ReadOnlyMemory<float> Vec(params float[] values) => values;

	[Test]
	public async Task Append_Increments_Count_And_Returns_New_Count() {
		var snapshot = new ActiveSnapshot(4);
		await Assert.That(snapshot.Append(1, Vec(1, 0))).IsEqualTo(1);
		await Assert.That(snapshot.Append(2, Vec(0, 1))).IsEqualTo(2);
		await Assert.That(snapshot.Count).IsEqualTo(2);
	}

	[Test]
	public async Task TryGet_Finds_Appended_And_Misses_Unknown() {
		var snapshot = new ActiveSnapshot(4);
		snapshot.Append(7, Vec(1, 2, 3));

		await Assert.That(snapshot.TryGet(7, out var vector)).IsTrue();
		await Assert.That(vector.Length).IsEqualTo(3);
		await Assert.That(snapshot.TryGet(8, out _)).IsFalse();
	}

	[Test]
	public async Task Extract_Returns_Buffered_Prefix() {
		var snapshot = new ActiveSnapshot(4);
		snapshot.Append(1, Vec(1));
		snapshot.Append(2, Vec(2));

		var (ids, vecs) = snapshot.Extract();
		await Assert.That(ids.Length).IsEqualTo(2);
		await Assert.That(vecs.Length).IsEqualTo(2);
		await Assert.That(ids.Span[0]).IsEqualTo(1ul);
		await Assert.That(ids.Span[1]).IsEqualTo(2ul);
	}

	[Test]
	public async Task Search_Ranks_Nearest_By_Cosine_Distance() {
		var snapshot = new ActiveSnapshot(4);
		snapshot.Append(1, Vec(1, 0)); // identical to the query → distance 0
		snapshot.Append(2, Vec(0, 1)); // orthogonal → distance 1

		var matches = new NearestMatches();
		snapshot.Search(Vec(1, 0).Span, matches);

		await Assert.That(matches.Nearest(10).SequenceEqual([1ul, 2ul])).IsTrue();
	}

	[Test]
	public async Task Can_Fill_To_Capacity() {
		var snapshot = new ActiveSnapshot(3);
		snapshot.Append(1, Vec(1));
		snapshot.Append(2, Vec(2));
		await Assert.That(snapshot.Append(3, Vec(3))).IsEqualTo(3);
		await Assert.That(snapshot.Count).IsEqualTo(3);
	}
}
