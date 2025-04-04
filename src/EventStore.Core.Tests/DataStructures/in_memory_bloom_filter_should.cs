using System;
using EventStore.Core.DataStructures.ProbabilisticFilter;
using NUnit.Framework;

namespace EventStore.Core.Tests.DataStructures {
	[TestFixture]
	public class bloom_filter_should {
		[Test]
		public void always_return_true_if_an_item_was_added() {
			for (int n = 1; n <= 1000; n++) {
				for (double p = 0.1; p > 1.0e-7; p /= 10.0) {
					InMemoryBloomFilter filter = new InMemoryBloomFilter(n, p);

					//no items added yet
					for (int i = 0; i <= n; i++) {
						Assert.IsFalse(filter.MightContain(i));
					}

					//add the items
					for (int i = 0; i <= n; i++) {
						filter.Add(i);
					}

					//all the items should exist
					for (int i = 0; i <= n; i++) {
						Assert.IsTrue(filter.MightContain(i));
					}
				}
			}
		}

		[Test, Category("LongRunning")]
		public void always_return_true_if_an_item_was_added_for_large_n() {
			int n = 1234567;
			double p = 1.0e-6;

			InMemoryBloomFilter filter = new InMemoryBloomFilter(n, p);

			//no items added yet
			for (int i = 0; i <= n; i++) {
				Assert.IsFalse(filter.MightContain(i));
			}

			//add the items
			for (int i = 0; i <= n; i++) {
				filter.Add(i);
			}

			//all the items should exist
			for (int i = 0; i <= n; i++) {
				Assert.IsTrue(filter.MightContain(i));
			}
		}

		[Test]
		public void support_adding_large_values() {
			int n = 1234567;
			double p = 1.0e-6;

			InMemoryBloomFilter filter = new InMemoryBloomFilter(n, p);
			long[] items = {
				192389123812L, 286928492L, 27582928698L, 72669175482L, 1738996371L, 939342020387L, 37253255484L,
				346536436L, 123921398432L, 8324982394329432L, 183874782348723874L, long.MaxValue
			};

			//no items added yet
			for (int i = 0; i < items.Length; i++) {
				Assert.IsFalse(filter.MightContain(items[i]));
			}

			//add the items
			for (int i = 0; i < items.Length; i++) {
				filter.Add(items[i]);
			}

			//all the items should exist
			for (int i = 0; i < items.Length; i++) {
				Assert.IsTrue(filter.MightContain(items[i]));
			}

			//all the neighbouring items should probably not exist
			for (int i = 0; i < items.Length; i++) {
				Assert.IsFalse(filter.MightContain(items[i] - 1));
				Assert.IsFalse(filter.MightContain(items[i] + 1));
			}
		}

		[Test]
		public void have_false_positives_with_probability_p() {
			for (int n = 1; n <= 1000; n++) {
				for (double p = 0.1; p > 1.0e-7; p /= 10.0) {
					InMemoryBloomFilter filter = new InMemoryBloomFilter(n, p);

					//add only odd numbers
					for (int i = 1; i <= n; i += 2) {
						filter.Add(i);
					}

					//expected number of false positives
					int expectedFalsePositives = (int)Math.Ceiling(n * p / 2.0);

					//none of these items should exist but there may be some false positives
					int falsePositives = 0;
					for (int i = 2; i <= n; i += 2) {
						if (filter.MightContain(i)) {
							falsePositives++;
						}
					}

					if (falsePositives > 0)
						Console.Out.WriteLine("n: {0}, p:{1}. Found {2} false positives. Expected false positives: {3}",
							n, p, falsePositives, expectedFalsePositives);

					Assert.LessOrEqual(falsePositives, expectedFalsePositives);
				}
			}
		}

		[Test]
		public void have_false_positives_with_probability_p_for_large_n() {
			int n = 1234567;

			for (double p = 0.1; p > 1.0e-7; p /= 10.0) {
				InMemoryBloomFilter filter = new InMemoryBloomFilter(n, p);

				//add only odd numbers
				for (int i = 1; i <= n; i += 2) {
					filter.Add(i);
				}

				//expected number of false positives
				int expectedFalsePositives = (int)Math.Ceiling(n * p / 2.0);

				//none of these items should exist but there may be some false positives
				int falsePositives = 0;
				for (int i = 2; i <= n; i += 2) {
					if (filter.MightContain(i)) {
						falsePositives++;
					}
				}

				if (falsePositives > 0)
					Console.Out.WriteLine("n: {0}, p:{1}. Found {2} false positives. Expected false positives: {3}", n,
						p, falsePositives, expectedFalsePositives);
				Assert.LessOrEqual(falsePositives, expectedFalsePositives);
			}
		}

		[Test]
		public void throw_argumentoutofrangeexception_when_given_non_positive_n() {
			Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryBloomFilter(0, 0.1));
			Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryBloomFilter(-1, 0.1));
		}

		[Test]
		public void throw_argumentoutofrangeexception_when_given_non_positive_p() {
			Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryBloomFilter(1, 0.0));
			Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryBloomFilter(1, -0.1));
		}

		[Test]
		public void throw_argumentoutofrangeexception_when_number_of_bits_too_large() {
			Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryBloomFilter(123456789, 0.0000000001));
		}

		[Test]
		public void correctly_convert_long_to_bytes() {
			for (long i = -1000; i <= 1000; i++) {
				byte[] bytes = InMemoryBloomFilter.toBytes(i);
				Assert.AreEqual(8, bytes.Length);
				long v = 0;

				for (int j = 7; j >= 0; j--) {
					v <<= 8;
					v |= bytes[j];
				}

				Assert.AreEqual(i, v);
			}

			long[] nums = {
				long.MaxValue, long.MinValue, 0, 192389123812L, 286928492L, 27582928698L, 72669175482L, 1738996371L,
				939342020387L, 37253255484L, 346536436L, 123921398432L, 8324982394329432L, 183874782348723874L
			};
			for (long i = 0; i < nums.Length; i++) {
				byte[] bytes = InMemoryBloomFilter.toBytes(nums[i]);
				Assert.AreEqual(8, bytes.Length);
				long v = 0;
				for (int j = 7; j >= 0; j--) {
					v <<= 8;
					v |= bytes[j];
				}

				Assert.AreEqual(nums[i], v);
			}
		}
	}
}
