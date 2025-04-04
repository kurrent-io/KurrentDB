using System;
using EventStore.Common.Utils;
using EventStore.Core.Index.Hashes;

namespace EventStore.Core.DataStructures.ProbabilisticFilter {
	public class InMemoryBloomFilter : IProbabilisticFilter<long> {
		/*
		    Bloom filter implementation based on the following paper by Adam Kirsch and Michael Mitzenmacher:
		    "Less Hashing, Same Performance: Building a Better Bloom Filter"
		    https://www.eecs.harvard.edu/~michaelm/postscripts/rsa2008.pdf

		    Only two 32-bit hash functions can be used to simulate additional hash functions of the form g(x) = h1(x) + i*h2(x)
		*/
		int m; //number of bits
		int k; //number of hash functions
		ulong[] bits; //bit array
		const int LONG_SIZE = sizeof(long); //64 bits

		public int NumBits {
			get { return m; }
		}

		public int NumHashFunctions {
			get { return k; }
		}

		private static IHasher hasher1 = new XXHashUnsafe(), hasher2 = new Murmur3AUnsafe();

		public InMemoryBloomFilter(int n, double p) {
			Ensure.Positive(n, "n");
			if (p <= 0.0 || p >= 0.5)
				throw new ArgumentOutOfRangeException("p", "p should be between 0 and 0.5 exclusive");

			//calculate number of hash functions to use
			k = (int)Math.Ceiling(-Math.Log(p) / Math.Log(2));
			k = Math.Max(2, k);

			//calculate number of bits required
			long m = (long)Math.Ceiling(-n * Math.Log(p) / Math.Log(2) / Math.Log(2));
			long buckets = m / LONG_SIZE;
			if (m % LONG_SIZE != 0)
				buckets++;
			if (m > Int32.MaxValue) {
				throw new ArgumentOutOfRangeException("p",
					"calculated number of bits, m, is too large: " + m + ". please choose a larger value of p.");
			}

			this.m = (int)m;
			bits = new ulong[buckets];
		}

		public void Add(long item) {
			byte[] bytes = toBytes(item);
			int hash1 = (int)hasher1.Hash(bytes);
			int hash2 = (int)hasher2.Hash(bytes);

			int hash = hash1;
			for (int i = 0; i < k; i++) {
				hash += hash2;
				hash &= Int32.MaxValue; //make positive
				int idx = (hash % m);
				bits[idx / LONG_SIZE] |= (1UL << (idx % LONG_SIZE));
			}
		}

		public bool MightContain(long item) {
			byte[] bytes = toBytes(item);
			int hash1 = (int)hasher1.Hash(bytes);
			int hash2 = (int)hasher2.Hash(bytes);

			int hash = hash1;
			for (int i = 0; i < k; i++) {
				hash += hash2;
				hash &= Int32.MaxValue; //make positive
				int idx = (hash % m);
				if ((bits[idx / LONG_SIZE] & (1UL << (idx % LONG_SIZE))) == 0)
					return false;
			}

			return true;
		}

		public static byte[] toBytes(long value) {
			byte[] bytes = new byte[8];
			for (int i = 0; i < 8; i++) {
				bytes[i] |= (byte)(value & 0xFF);
				value >>= 8;
			}

			return bytes;
		}
	}
}
