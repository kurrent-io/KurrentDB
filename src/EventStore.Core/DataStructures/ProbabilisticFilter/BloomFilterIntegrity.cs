// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.InteropServices;
using EventStore.Common.Utils;
using EventStore.Core.Index.Hashes;
using Serilog;

namespace EventStore.Core.DataStructures.ProbabilisticFilter;

// This class is responsible for maintaining and validating a 4 byte hash at the end
// of the cache line.
public class BloomFilterIntegrity {
	public const int CacheLineSize = 64;
	public const int HashSize = 4;
	// amount of filter that is marked dirty for flushing
	public const int PageSize = 8 * 1024;

	private const int UintHashIndex = (CacheLineSize - HashSize) / HashSize;
	static readonly ILogger Log = Serilog.Log.ForContext<BloomFilterIntegrity>();

	private static readonly IHasher _hasher = new XXHashUnsafe(0xc58f1a7b);

	public static void WriteHash(Span<byte> cacheLine) {
		Ensure.Equal(CacheLineSize, cacheLine.Length, "cacheline length");

		if (IsAllOnes(cacheLine)) {
			// corrupted line made safe, dont make it valid again by writing the hash
			return;
		}

		var hash = _hasher.Hash(cacheLine[..^HashSize]);
		var uints = MemoryMarshal.Cast<byte, uint>(cacheLine);
		uints[UintHashIndex] = hash;
	}

	// If the hash is correct, return true.
	// If the hash is not correct, but can be corrected then correct it and return true.
	// If the hash is not correct and cannot be corrected then set all the bits in the
	// cacheline and return false. The data is made safe for the bloom filter.
	public static bool ValidateHash(Span<byte> cacheLine) {
		Ensure.Equal(CacheLineSize, cacheLine.Length, "cacheline length");

		if (IsAllOnes(cacheLine)) {
			// all bytes 0xFFs. Corrupted data that has been made safe.
			return false;
		}

		if (IsAllZeros(cacheLine)) {
			// all bytes 0x00s. Empty data
			return true;
		}

		// check the hash
		var cacheToHash = cacheLine[..^HashSize];
		var hash = _hasher.Hash(cacheToHash);
		var targetHash = MemoryMarshal.Cast<byte, uint>(cacheLine)[UintHashIndex];
		if (hash == targetHash)
			return true;

		// corrupt, try to recover.
		if (CanRehash(cacheToHash, targetHash)) {
			WriteHash(cacheLine);
			Log.Verbose("Corrected a hash");
			return true;
		}

		// corrupt and could not recover, set all bytes to 0xFFs to make safe
		var ulongs = MemoryMarshal.Cast<byte, ulong>(cacheLine);
		for (int i = 0; i < ulongs.Length; i++) {
			ulongs[i] = ulong.MaxValue;
		}

		return false;
	}

	// The hash can be wrong simply because the flush happened between when
	// we set the bit and set the new hash. We can detect this by unsetting
	// a single bit that is set and see if it produces the hash that is recorded
	// in the cacheline. If so, then we can have confidence that the data is
	// actually correct and we can simply rehash it.
	private static bool CanRehash(ReadOnlySpan<byte> dataToHash, uint targetHash) {
		var copy = dataToHash.ToArray().AsSpan();

		for (var byteIndex1 = 0; byteIndex1 < copy.Length; byteIndex1++) {
			for (var bitIndex1 = 0; bitIndex1 < 8; bitIndex1++) {
				if (!copy[byteIndex1].IsBitSet(bitIndex1))
					continue;

				copy[byteIndex1] = copy[byteIndex1].UnsetBit(bitIndex1);

				if (_hasher.Hash(copy) == targetHash)
					return true;

				copy[byteIndex1] = copy[byteIndex1].SetBit(bitIndex1);
			}
		}

		return false;
	}

	private static bool IsAllZeros(ReadOnlySpan<byte> cacheLine) {
		var ulongs = MemoryMarshal.Cast<byte, ulong>(cacheLine);
		for (int i = 0; i < ulongs.Length; i++) {
			if (ulongs[i] != ulong.MinValue)
				return false;
		}
		return true;
	}

	private static bool IsAllOnes(ReadOnlySpan<byte> cacheLine) {
		var ulongs = MemoryMarshal.Cast<byte, ulong>(cacheLine);
		for (int i = 0; i < ulongs.Length; i++) {
			if (ulongs[i] != ulong.MaxValue)
				return false;
		}
		return true;
	}
}
