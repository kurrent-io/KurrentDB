// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Index.Hashes;

namespace EventStore.Core.Tests.Hashes;

/// <summary>
/// Adapted Verification and Sanity checks from SMHasher
/// http://code.google.com/p/smhasher/
/// </summary>
public static class SMHasher {
	//-----------------------------------------------------------------------------
	// This should hopefully be a thorough and unambiguous test of whether a hash
	// is correctly implemented on a given platform
	public static bool VerificationTest(IHasher hasher, uint expected) {
		const int hashBytes = 4;

		var key = new byte[256];
		var hashes = new byte[hashBytes * 256];

		// Hash keys of the form {0}, {0,1}, {0,1,2}... up to N=255,using 256-N as
		// the seed
		for (int i = 0; i < 256; i++) {
			key[i] = (byte)i;

			var hash = hasher.Hash(key, 0, (uint)i, (uint)(256 - i));
			Buffer.BlockCopy(BitConverter.GetBytes(hash), 0, hashes, i * hashBytes, hashBytes);
		}

		// Then hash the result array

		var verification = hasher.Hash(hashes, 0, hashBytes * 256, 0);

		// The first four bytes of that hash, interpreted as a little-endian integer, is our
		// verification value

		//uint verification = (final[0] << 0) | (final[1] << 8) | (final[2] << 16) | (final[3] << 24);


		//----------

		if (expected != verification) {
			Console.WriteLine("Verification value 0x{0:X8} : Failed! (Expected 0x{1:X8})", verification, expected);
			return false;
		} else {
			Console.WriteLine("Verification value 0x{0:X8} : Passed!", verification);
			return true;
		}
	}

	//----------------------------------------------------------------------------
	// Basic sanity checks:
	//     - A hash function should not be reading outside the bounds of the key.
	//     - Flipping a bit of a key should, with overwhelmingly high probability, result in a different hash.
	//     - Hashing the same key twice should always produce the same result.
	//     - The memory alignment of the key should not affect the hash result.
	public static bool SanityTest(IHasher hasher) {
		var rnd = new Random(883741);

		bool result = true;

		const int reps = 10;
		const int keymax = 256;
		const int pad = 16;
		const int buflen = keymax + pad * 3;

		byte[] buffer1 = new byte[buflen];
		byte[] buffer2 = new byte[buflen];
		//----------
		for (int irep = 0; irep < reps; irep++) {
			if (irep % (reps / 10) == 0) Console.Write(".");

			for (int len = 4; len <= keymax; len++) {
				for (int offset = pad; offset < pad * 2; offset++) {
//                        byte* key1 = &buffer1[pad];
//                        byte* key2 = &buffer2[pad+offset];

					rnd.NextBytes(buffer1);
					rnd.NextBytes(buffer2);

					//memcpy(key2, key1, len);
					Buffer.BlockCopy(buffer2, pad + offset, buffer1, pad, len);

					// hash1 = hash(key1, len, 0)
					var hash1 = hasher.Hash(buffer1, pad, (uint)len, 0);

					for (int bit = 0; bit < (len * 8); bit++) {
						// Flip a bit, hash the key -> we should get a different result.
						//Flipbit(key2,len,bit);
						Flipbit(buffer2, pad + offset, len, bit);
						var hash2 = hasher.Hash(buffer2, pad + offset, (uint)len, 0);

						if (hash1 == hash2)
							result = false;

						// Flip it back, hash again -> we should get the original result.
						//flipbit(key2,len,bit);
						Flipbit(buffer2, pad + offset, len, bit);
						//hash(key2, len, 0, hash2);
						hash2 = hasher.Hash(buffer2, pad + offset, (uint)len, 0);

						if (hash1 != hash2)
							result = false;
					}
				}
			}
		}

		return result;
	}

	private static void Flipbit(byte[] array, int offset, int len, int bit) {
		var byteNum = bit >> 3;
		bit = bit & 0x7;

		if (byteNum < len)
			array[offset + byteNum] ^= (byte)(1 << bit);
	}
}
