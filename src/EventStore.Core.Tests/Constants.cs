// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Settings;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.Util;

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
	
