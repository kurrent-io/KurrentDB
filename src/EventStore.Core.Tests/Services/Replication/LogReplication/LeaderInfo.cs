// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Cluster;
using EventStore.Core.Services.Replication;
using EventStore.Core.Services.Storage;
using EventStore.Core.Services.Storage.EpochManager;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.Tests.Services.Replication.LogReplication;

internal record LeaderInfo<TStreamId> {
	public TFChunkDb Db { get; init; }
	public IPublisher Publisher { get; init; }
	public LeaderReplicationService ReplicationService { get; init; }
	public IEpochManager EpochManager { get; init; }
	public MemberInfo MemberInfo { get; init; }
	public StorageWriterService<TStreamId> Writer { get; init; }
}
