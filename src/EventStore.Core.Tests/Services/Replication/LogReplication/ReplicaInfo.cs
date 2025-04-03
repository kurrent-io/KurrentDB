// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using EventStore.Core.Bus;
using EventStore.Core.Services.Replication;
using EventStore.Core.Services.Storage;
using EventStore.Core.Services.Storage.EpochManager;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.Tests.Services.Replication.LogReplication;

internal record ReplicaInfo<TStreamId> {
	public TFChunkDb Db { get; init; }
	public IPublisher Publisher { get; init; }
	public ReplicaService ReplicaService { get; init; }
	public IEpochManager EpochManager { get; init; }
	public StorageWriterService<TStreamId> Writer { get; init; }
	public Func<int> GetNumWriterFlushes { get; init; }
	public ReplicationInterceptor ReplicationInterceptor { get; init; }
	public AutoResetEvent ConnectionEstablished { get; init; }
	public Func<long> GetReplicationPosition { get; init; }
	public Action ResetSubscription { get; init; }
}
