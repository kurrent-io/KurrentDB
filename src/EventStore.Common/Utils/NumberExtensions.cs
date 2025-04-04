using System;

namespace EventStore.Common.Utils {
	public static class NumberExtensions {
		public static long RoundDownToMultipleOf(this long x, int multiple) {
			return (x / multiple) * multiple;
		}

		public static long RoundUpToMultipleOf(this long x, int multiple) {
			var ret = x.RoundDownToMultipleOf(multiple);
			if (ret != x) {
				ret += multiple;
			}
			return ret;
		}

		public static int RoundDownToMultipleOf(this int x, int multiple) {
			return (x / multiple) * multiple;
		}

		public static int RoundUpToMultipleOf(this int x, int multiple) {
			var ret = x.RoundDownToMultipleOf(multiple);
			if (ret != x) {
				ret += multiple;
			}
			return ret;
		}

		public static long ScaleByWeight(this long x, int weight, int totalWeight) {
			if (totalWeight <= 0)
				throw new ArgumentOutOfRangeException(nameof(totalWeight));

			if (weight < 0 || weight > totalWeight)
				throw new ArgumentOutOfRangeException(nameof(weight));

			return (long)((double)x * weight / totalWeight);
		}

		public static long ScaleByPercent(this long x, int percent) =>
			ScaleByWeight(x, percent, 100);

	}
}
