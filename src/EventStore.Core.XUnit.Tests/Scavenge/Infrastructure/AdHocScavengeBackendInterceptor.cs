// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

public class AdHocScavengeBackendInterceptor<TStreamId> : IScavengeStateBackend<TStreamId> {
	private readonly IScavengeStateBackend<TStreamId> _wrapped;
	public AdHocScavengeBackendInterceptor(IScavengeStateBackend<TStreamId> wrapped) {
		_wrapped = wrapped;
		CollisionStorage = wrapped.CollisionStorage;
		Hashes = wrapped.Hashes;
		MetaStorage = wrapped.MetaStorage;
		MetaCollisionStorage = wrapped.MetaCollisionStorage;
		OriginalStorage = wrapped.OriginalStorage;
		OriginalCollisionStorage = wrapped.OriginalCollisionStorage;
		CheckpointStorage = wrapped.CheckpointStorage;
		ChunkTimeStampRanges = wrapped.ChunkTimeStampRanges;
		ChunkWeights = wrapped.ChunkWeights;
		TransactionManager = wrapped.TransactionManager;
	}

	public void Dispose() => _wrapped.Dispose();
	public void LogStats() => _wrapped.LogStats();
	public IScavengeMap<TStreamId, Unit> CollisionStorage { get; set; }
	public IScavengeMap<ulong, TStreamId> Hashes { get; set; }
	public IMetastreamScavengeMap<ulong> MetaStorage { get; set; }
	public IMetastreamScavengeMap<TStreamId> MetaCollisionStorage { get; set; }
	public IOriginalStreamScavengeMap<ulong> OriginalStorage { get; set; }
	public IOriginalStreamScavengeMap<TStreamId> OriginalCollisionStorage { get; set; }
	public IScavengeMap<Unit, ScavengeCheckpoint> CheckpointStorage { get; set; }
	public IScavengeMap<int, ChunkTimeStampRange> ChunkTimeStampRanges { get; set; }
	public IChunkWeightScavengeMap ChunkWeights { get; set; }
	public ITransactionManager TransactionManager { get; set; }
}
