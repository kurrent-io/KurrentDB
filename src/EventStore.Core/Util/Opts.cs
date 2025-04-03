// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Common.Options;
using EventStore.Core.TransactionLog.Chunks;

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
