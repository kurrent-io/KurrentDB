// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.TransactionLog.Scavenging.InMemory;

public class InMemoryScavengeBackend : IScavengeStateBackend<string> {
	public InMemoryScavengeBackend() {
		var checkpointStore = new InMemoryScavengeMap<Unit, ScavengeCheckpoint>();

		ChunkTimeStampRanges = new InMemoryScavengeMap<int, ChunkTimeStampRange>();
		CollisionStorage = new InMemoryScavengeMap<string, Unit>();
		Hashes = new InMemoryScavengeMap<ulong, string>();
		MetaStorage = new InMemoryMetastreamScavengeMap<ulong>();
		MetaCollisionStorage = new InMemoryMetastreamScavengeMap<string>();
		OriginalStorage = new InMemoryOriginalStreamScavengeMap<ulong>();
		OriginalCollisionStorage = new InMemoryOriginalStreamScavengeMap<string>();
		CheckpointStorage = checkpointStore;
		ChunkWeights = new InMemoryChunkWeightScavengeMap();
		TransactionManager = new InMemoryTransactionManager(checkpointStore);
	}

	public IScavengeMap<int, ChunkTimeStampRange> ChunkTimeStampRanges { get; }

	public IScavengeMap<string, Unit> CollisionStorage { get; }

	public IScavengeMap<ulong, string> Hashes { get; }

	public IMetastreamScavengeMap<ulong> MetaStorage { get; }

	public IMetastreamScavengeMap<string> MetaCollisionStorage { get; }

	public IOriginalStreamScavengeMap<ulong> OriginalStorage { get; }

	public IOriginalStreamScavengeMap<string> OriginalCollisionStorage { get; }

	public IScavengeMap<Unit, ScavengeCheckpoint> CheckpointStorage { get; }

	public IChunkWeightScavengeMap ChunkWeights { get; }

	public ITransactionManager TransactionManager { get; }

	public void Dispose() {
	}

	public void LogStats() {
	}
}
