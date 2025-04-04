namespace EventStore.Core.Util {
	public static class Opts {
		public const int ConnectionPendingSendBytesThresholdDefault = 10 * 1024 * 1024;

		public const int ConnectionQueueSizeThresholdDefault = 50000;

		public const int HashCollisionReadLimitDefault = 100;

		public const int ChunkInitialReaderCountDefault = 5;

		public const bool FaultOutOfOrderProjectionsDefault = false;

		public const int ProjectionsQueryExpiryDefault = 5;

		public const byte IndexBitnessVersionDefault = Index.PTableVersions.IndexV4;

		public static readonly string AuthenticationTypeDefault = "internal";

		public const bool SkipIndexScanOnReadsDefault = false;

		public const long StreamExistenceFilterSizeDefault = 256_000_000;

		public const int ScavengeBackendPageSizeDefault = 16 * 1024;

		public const int ScavengeBackendCacheSizeDefault = 64 * 1024 * 1024;

		public const int ScavengeHashUsersCacheCapacityDefault = 100_000;


	}
}
