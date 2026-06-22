// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Numerics.Tensors;

namespace KurrentDB.Kontext.Indexing.USearch;

/// <summary>
/// The fixed-capacity L0 write buffer, published to readers as a volatile reference by the store.
///
/// Thread safety:
/// - Append, Extract: single writer only (the indexing subscription). Append's count read + slot
///   write are not atomic against a concurrent Append.
/// - Count, TryGet, Search: safe on any thread, concurrently with the single writer. They read
///   Count with acquire semantics, so they only ever observe fully-written slots.
/// </summary>
sealed class ActiveSnapshot(int capacity) {
	ulong[] Ids { get; } = new ulong[capacity];
	ReadOnlyMemory<float>[] Vecs { get; } = new ReadOnlyMemory<float>[capacity];
	int _count;

	public int Count => Volatile.Read(ref _count);

	public int Append(ulong id, ReadOnlyMemory<float> vector) {
		var n = _count;
		Ids[n] = id;
		Vecs[n] = vector;
		Volatile.Write(ref _count, n + 1); // publish last (release) so readers see the slot writes
		return n + 1;
	}

	public bool TryGet(ulong id, out ReadOnlyMemory<float> vector) {
		var count = Count;
		for (var i = 0; i < count; i++) {
			if (Ids[i] == id) {
				vector = Vecs[i];
				return true;
			}
		}
		vector = default;
		return false;
	}

	public (ReadOnlyMemory<ulong> Ids, ReadOnlyMemory<ReadOnlyMemory<float>> Vecs) Extract() {
		var count = Count;
		return (Ids.AsMemory(0, count), Vecs.AsMemory(0, count));
	}

	// Brute-force cosine scan over the buffered vectors.
	public void Search(ReadOnlySpan<float> query, NearestMatches matches) {
		var count = Count;
		for (var i = 0; i < count; i++)
			matches.Offer(Ids[i], CosineDistance(query, Vecs[i].Span));
	}

	static float CosineDistance(ReadOnlySpan<float> query, ReadOnlySpan<float> vector) {
		var similarity = TensorPrimitives.CosineSimilarity(query, vector);
		return float.IsNaN(similarity) ? 1f : 1f - similarity;
	}
}
