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
}
