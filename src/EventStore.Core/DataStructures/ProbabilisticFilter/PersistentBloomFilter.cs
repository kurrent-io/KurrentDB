// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using EventStore.Core.Index.Hashes;
using Serilog;

namespace EventStore.Core.DataStructures.ProbabilisticFilter;

// the bloom filter has a pluggable strategy for persisting data. the IPersistenceStrategy
// provides us with a BloomFilterAccessor that we can use to manipulate the data.
// we deal with actually bloom filter hashing in here and synchronise the access to the data.
public class PersistentBloomFilter : IProbabilisticFilter, IDisposable {
	/*
	    Bloom filter implementation based on the following paper by Adam Kirsch and Michael Mitzenmacher:
	    "Less Hashing, Same Performance: Building a Better Bloom Filter"
	    https://www.eecs.harvard.edu/~michaelm/postscripts/rsa2008.pdf

	    Only two 32-bit hash functions can be used to simulate additional hash functions of the form g(x) = h1(x) + i*h2(x)
	*/

	protected static readonly ILogger Log = Serilog.Log.ForContext<PersistentBloomFilter>();
	public const double RecommendedFalsePositiveProbability = 0.02;
	private const int NumHashFunctions = 6;

	private readonly IHasher[] _hashers = {
		new XXHashUnsafe(seed: 0xC0015EEDU),
		new Murmur3AUnsafe(seed: 0xC0015EEDU),
		new XXHashUnsafe(seed: 0x13375EEDU),
		new Murmur3AUnsafe(seed: 0x13375EEDU)
	};

	private readonly Header _header;
	private readonly object _writeLock = new();
	private readonly BloomFilterAccessor _data;
	private readonly IPersistenceStrategy _persistenceStrategy;

	/// <summary>
	/// Bloom filter implementation which uses a file for persistence.
	/// </summary>
	/// Add and MightContain can be called concurrently on different threads.
	/// If the call to Add is complete then a call to MightContain will find the added value.
	public PersistentBloomFilter(
		IPersistenceStrategy persistenceStrategy,
		int corruptionRebuildCount = 0) {

		try {
			_persistenceStrategy = persistenceStrategy;
			// initialisation and verification can involve writing
			// there will be no contention since we are in the constructor, but
			// we want to ensure our writes are available to the readers
			lock (_writeLock) {
				_persistenceStrategy.Init();
				_data = _persistenceStrategy.DataAccessor;
				var numBits = _data.LogicalFilterSizeBits;
				if (_persistenceStrategy.Create) {
					_header = new Header() {
						Version = Header.CurrentVersion,
						CorruptionRebuildCount = corruptionRebuildCount,
						NumBits = numBits
					};

					_persistenceStrategy.WriteHeader(_header);
				} else {
					_header = _persistenceStrategy.ReadHeader();
					if (_header.NumBits != numBits) {
						throw new SizeMismatchException(
							$"The configured number of bytes ({(numBits / 8):N0}) does not match the number of bytes in file header ({(_header.NumBits / 8):N0}).");
					}
					Verify(0.05);
				}
			}
		} catch {
			Dispose();
			throw;
		}
	}

	public int CorruptionRebuildCount => _header.CorruptionRebuildCount;

	/// <summary>
	/// Calculates optimal number of items in the bloom filter for a specific false positive probability, p
	/// </summary>
	/// <param name="p">Desired false positive probability</param>
	/// <returns></returns>
	public long CalculateOptimalNumItems(double p = RecommendedFalsePositiveProbability) {
		return Convert.ToInt64(Math.Floor(
			Math.Log(1 - Math.Pow(p, 1.0 / NumHashFunctions)) /
			Math.Log(Math.Pow(1 - 1.0 / _data.LogicalFilterSizeBits, NumHashFunctions)
		)));
	}

	public bool MightContain(ReadOnlySpan<byte> bytes) {
		long hash1 = ((long)_hashers[0].Hash(bytes) << 32) | _hashers[1].Hash(bytes);
		long hash2 = ((long)_hashers[2].Hash(bytes) << 32) | _hashers[3].Hash(bytes);

		// guarantee our LOADs are executed now and not prior.
		// dual with the memory barrier implicit in the Add(...) method
		Thread.MemoryBarrier();

		long hash = hash1;
		for (int i = 0; i < NumHashFunctions; i++) {
			hash += hash2;
			hash &= long.MaxValue; // make non-negative
			long bitPosition = hash % _data.LogicalFilterSizeBits;
			if (!_data.IsBitSet(bitPosition))
				return false;
		}

		return true;
	}

	public void Add(ReadOnlySpan<byte> bytes) {
		long hash1 = ((long)_hashers[0].Hash(bytes) << 32) | _hashers[1].Hash(bytes);
		long hash2 = ((long)_hashers[2].Hash(bytes) << 32) | _hashers[3].Hash(bytes);

		// guarantee only one writer
		lock (_writeLock) {
			long hash = hash1;
			// possible SIMD optimisation?
			for (int i = 0; i < NumHashFunctions; i++) {
				hash += hash2;
				hash &= long.MaxValue; // make non-negative
				long bitPosition = hash % _data.LogicalFilterSizeBits;
				// Consider returning the dirty page from set bit and calling the
				// persistence strategy directly from here to remove the callback delegate
				_data.SetBit(bitPosition);
			}
			// lock release guarantees our STOREs are executed before we return
			// todo: confirm is this is true on ARM
		}
	}

	// not re-entrant
	public void Flush() {
		// need to be sure the read operations were not done in advance of this point
		// not 100% sure, however, that the barrier is required.
		Thread.MemoryBarrier();
		_persistenceStrategy.Flush();
	}

	public void Dispose() {
		_persistenceStrategy.Dispose();
	}

	// corruptionThreshold = 0.05 => tolerate up to 5% corruption
	public void Verify(double corruptionThreshold) {
		lock (_writeLock) {
			_data.Verify(_header.CorruptionRebuildCount, corruptionThreshold);
		}
	}
}
