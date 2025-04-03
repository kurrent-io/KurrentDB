// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.InteropServices;
using EventStore.Common.Utils;
using EventStore.Core.Index.Hashes;

namespace EventStore.Core.DataStructures.ProbabilisticFilter;

// this class exists to deal with the fact that v3 stores strings in the bloom filter
// but v2 stores hashes (to make it faster to rebuild from the standard index)
//
// if you provide a ILongHasher then strings are hashed using it and the resulting ulong
// added to the filter. if you add a ulong directly, it must be the result of the same hash.
public class PersistentStreamBloomFilter : PersistentBloomFilter {
	private readonly ILongHasher<string> _hasher;

	public PersistentStreamBloomFilter(
		IPersistenceStrategy persistenceStrategy,
		ILongHasher<string> hasher,
		int corruptionRebuildCount = 0) :

		base(persistenceStrategy, corruptionRebuildCount) {
		_hasher = hasher;
	}

	public void Add(string stream) {
		if (_hasher != null) {
			var hash = _hasher.Hash(stream);
			Add(GetSpan(ref hash));
		} else {
			Add(MemoryMarshal.AsBytes(stream.AsSpan()));
		}
	}

	public void Add(ulong streamHash) {
		Ensure.NotNull(_hasher, "Hasher");
		Add(GetSpan(ref streamHash));
	}

	public bool MightContain(string stream) {
		if (_hasher != null) {
			var hash = _hasher.Hash(stream);
			return MightContain(GetSpan(ref hash));
		} else {
			return MightContain(MemoryMarshal.AsBytes(stream.AsSpan()));
		}
	}

	private static ReadOnlySpan<byte> GetSpan(ref ulong streamHash) =>
		MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref streamHash, 1));
}
