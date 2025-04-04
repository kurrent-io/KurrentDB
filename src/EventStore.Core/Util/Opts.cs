// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

namespace EventStore.Core.Util;

public static class Opts {
	public const int ConnectionPendingSendBytesThresholdDefault = 10 * 1024 * 1024;

	public const int ConnectionQueueSizeThresholdDefault = 50000;

	public const int HashCollisionReadLimitDefault = 100;

	public const bool FaultOutOfOrderProjectionsDefault = false;

	public const int ProjectionsQueryExpiryDefault = 5;

	public const int MaxProjectionStateSizeDefault = int.MaxValue;

	public const byte IndexBitnessVersionDefault = Index.PTableVersions.IndexV4;

	public static readonly string AuthenticationTypeDefault = "internal";

	public const bool SkipIndexScanOnReadsDefault = false;

	public const long StreamExistenceFilterSizeDefault = 256_000_000;

	public const int ScavengeBackendPageSizeDefault = 16 * 1024;

	public const int ScavengeBackendCacheSizeDefault = 64 * 1024 * 1024;

	public const int ScavengeHashUsersCacheCapacityDefault = 100_000;


}
