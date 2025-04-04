// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Settings;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.Tests;

public class Constants {
	public const int PTableInitialReaderCount = ESConsts.PTableInitialReaderCount;

	public const int PTableMaxReaderCountDefault = 1 /* StorageWriter */
												   + 1 /* StorageChaser */
												   + 1 /* Projections */
												   + TFChunkScavenger.MaxThreadCount /* Scavenging (1 per thread) */
												   + 1 /* Subscription LinkTos resolving */
												   + 4 /* Reader Threads Count */
												   + 5 /* just in case reserve :) */;
}

