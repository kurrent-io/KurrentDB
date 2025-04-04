// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.TransactionLog.Scavenging;

namespace EventStore.Core.XUnit.Tests.Scavenge;

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
