// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Core.Settings;

public static class ESConsts {
	public const int PTableInitialReaderCount = 5;
	public const int MemTableEntryCount = 1000000;
	public const int IndexWriterCacheCapacity = 100_000;
	public const int TransactionMetadataCacheCapacity = 50000;
	public const int CommittedEventsMemCacheLimit = 8 * 1024 * 1024;
	public const int CachedEpochCount = 1000;
	public const int ReadRequestTimeout = 10000;
	public const bool PerformAdditionlCommitChecks = false;
	public const int MetaStreamMaxCount = 1;

	public const int CachedPrincipalCount = 1000;

	public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
	public static readonly TimeSpan HttpClientConnectionLifeTime = TimeSpan.FromMinutes(10);

	public const int UnrestrictedPendingSendBytes = 0;
	public const int MaxConnectionQueueSize = 50000;

	public const string DefaultIndexDirectoryName = "index";
	public const string StreamExistenceFilterDirectoryName = "stream-existence";
	public const string KontrollerDirectoryName = "kontroller";

	public const int KPlaneConnectionPoolCapacity = 32;

	// number of records to accumulate in wal before squash
	public const int KPlaneSnapshotDepth = 512;

	// renewal delay as a multiple of timeout
	public const double KPlaneRenewalRate = 0.5;

	// Normal HTTP timeout
	public const int KPlaneUnaryCallTimeoutMs = 30_000;
}
