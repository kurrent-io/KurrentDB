// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Index.Hashes;

public class XXHashUnsafe : IHasher, IHasher<string> {
	private readonly uint _seed;

	private const uint PRIME1 = 2654435761U;
	private const uint PRIME2 = 2246822519U;
	private const uint PRIME3 = 3266489917U;
	private const uint PRIME4 = 668265263U;
	private const int PRIME5 = 0x165667b1;

	public XXHashUnsafe(uint seed = 0xc58f1a7b) {
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
		if (len < 16)
			return HashSmall(data, len, seed);

		uint v1 = seed + PRIME1;
		uint v2 = v1 * PRIME2 + len;
		uint v3 = v2 * PRIME3;
		uint v4 = v3 * PRIME4;

		uint* p = (uint*)data;
		uint* limit = (uint*)(data + len - 16);

		while (p < limit) {
			v1 += Rotl32(v1, 13);
			v1 *= PRIME1;
			v1 += *p;
			p++;
			v2 += Rotl32(v2, 11);
			v2 *= PRIME1;
			v2 += *p;
			p++;
			v3 += Rotl32(v3, 17);
			v3 *= PRIME1;
			v3 += *p;
			p++;
			v4 += Rotl32(v4, 19);
			v4 *= PRIME1;
			v4 += *p;
			p++;
		}

		p = limit;
		v1 += Rotl32(v1, 17);
		v2 += Rotl32(v2, 19);
		v3 += Rotl32(v3, 13);
		v4 += Rotl32(v4, 11);
		v1 *= PRIME1;
		v2 *= PRIME1;
		v3 *= PRIME1;
		v4 *= PRIME1;
		v1 += *p;
		p++;
		v2 += *p;
		p++;
		v3 += *p;
		p++;
		v4 += *p;
		v1 *= PRIME2;
		v2 *= PRIME2;
		v3 *= PRIME2;
		v4 *= PRIME2;
		v1 += Rotl32(v1, 11);
		v2 += Rotl32(v2, 17);
		v3 += Rotl32(v3, 19);
		v4 += Rotl32(v4, 13);
		v1 *= PRIME3;
		v2 *= PRIME3;
		v3 *= PRIME3;
		v4 *= PRIME3;

		uint crc = v1 + Rotl32(v2, 3) + Rotl32(v3, 6) + Rotl32(v4, 9);
		crc ^= crc >> 11;
		crc += (PRIME4 + len) * PRIME1;
		crc ^= crc >> 15;
		crc *= PRIME2;
		crc ^= crc >> 13;
		return crc;
	}

	private unsafe static uint HashSmall(byte* data, uint len, uint seed) {
		byte* p = data;
		byte* bEnd = data + len;
		byte* limit = bEnd - 4;

		uint idx = seed + PRIME1;
		uint crc = PRIME5;

		while (p < limit) {
			crc += (*(uint*)p) + idx;
			idx++;
			crc += Rotl32(crc, 17) * PRIME4;
			crc *= PRIME1;
			p += 4;
		}

		while (p < bEnd) {
			crc += (*p) + idx;
			idx++;
			crc *= PRIME1;
			p++;
		}

		crc += len;

		crc ^= crc >> 15;
		crc *= PRIME2;
		crc ^= crc >> 13;
		crc *= PRIME3;
		crc ^= crc >> 16;

		return crc;
	}

	private static UInt32 Rotl32(UInt32 x, int r) {
		return (x << r) | (x >> (32 - r));
	}
}
