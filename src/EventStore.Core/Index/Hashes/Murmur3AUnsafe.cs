// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Index.Hashes;

public class Murmur3AUnsafe : IHasher, IHasher<string> {
	private readonly uint _seed;

	private const UInt32 c1 = 0xcc9e2d51;
	private const UInt32 c2 = 0x1b873593;

	public Murmur3AUnsafe(uint seed = 0xc58f1a7b) {
		_seed = seed;
	}

	public unsafe UInt32 Hash(string s) {
		fixed (char* input = s) {
			return Hash((byte*)input, (uint)s.Length * sizeof(char), _seed);
		}
	}

	public unsafe uint Hash(byte[] data) {
		fixed (byte* input = &data[0]) {
			return Hash(input, (uint)data.Length, _seed);
		}
	}

	public unsafe uint Hash(byte[] data, int offset, uint len, uint seed) {
		fixed (byte* input = &data[offset]) {
			return Hash(input, len, seed);
		}
	}

	public unsafe uint Hash(ReadOnlySpan<byte> data) {
		fixed (byte* input = data) {
			return Hash(input, (uint)data.Length, _seed);
		}
	}

	private unsafe static uint Hash(byte* data, uint len, uint seed) {
		UInt32 nblocks = len / 4;
		UInt32 h1 = seed;

		//----------
		// body

		UInt32 k1;
		UInt32* block = (UInt32*)data;
		for (UInt32 i = nblocks; i > 0; --i, ++block) {
			k1 = *block;

			k1 *= c1;
			k1 = Rotl32(k1, 15);
			k1 *= c2;

			h1 ^= k1;
			h1 = Rotl32(h1, 13);
			h1 = h1 * 5 + 0xe6546b64;
		}

		//----------
		// tail


		k1 = 0;
		uint rem = len & 3;
		byte* tail = (byte*)block;
		if (rem >= 3)
			k1 ^= (uint)(tail[2] << 16);
		if (rem >= 2)
			k1 ^= (uint)(tail[1] << 8);
		if (rem > 0) {
			k1 ^= tail[0];
			k1 *= c1;
			k1 = Rotl32(k1, 15);
			k1 *= c2;
			h1 ^= k1;
		}

		//----------
		// finalization

		h1 ^= len;

		h1 ^= h1 >> 16;
		h1 *= 0x85ebca6b;
		h1 ^= h1 >> 13;
		h1 *= 0xc2b2ae35;
		h1 ^= h1 >> 16;

		return h1;
	}

	private static UInt32 Rotl32(UInt32 x, int r) {
		return (x << r) | (x >> (32 - r));
	}
}
