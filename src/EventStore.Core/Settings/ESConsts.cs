// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;

namespace EventStore.Core.Settings;

public static class ESConsts {
	public const int PTableInitialReaderCount = 5;
	public const int MemTableEntryCount = 1000000;
	public const int IndexWriterCacheCapacity = 100_000;
	public const int TransactionMetadataCacheCapacity = 50000;
	public const int CommitedEventsMemCacheLimit = 8 * 1024 * 1024;
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
