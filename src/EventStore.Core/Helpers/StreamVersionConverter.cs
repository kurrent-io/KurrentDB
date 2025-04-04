namespace EventStore.Core.Helpers {
	public static class StreamVersionConverter {
		public static int Downgrade(long expectedVersion) {
			if (expectedVersion == long.MaxValue) {
				return int.MaxValue;
			}

			return (int)expectedVersion;
		}

		public static long Upgrade(long expectedVersion) {
			return expectedVersion == int.MaxValue ? long.MaxValue : expectedVersion;
		}
	}
}
