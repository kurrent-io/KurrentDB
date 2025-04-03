// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Data;

namespace EventStore.Core.TransactionLog.Scavenging.Interfaces;

public interface IScavengeState<TStreamId> :
	IScavengeStateForAccumulator<TStreamId>,
	IScavengeStateForCalculator<TStreamId>,
	IScavengeStateForIndexExecutor<TStreamId>,
	IScavengeStateForChunkMerger,
	IScavengeStateForChunkExecutor<TStreamId>,
	IScavengeStateForCleaner,
	IDisposable {

	void Init();

	bool TryGetCheckpoint(out ScavengeCheckpoint checkpoint);

	IEnumerable<TStreamId> AllCollisions();

	void LogStats();
}

// all the components use these
public interface IScavengeStateCommon {
	// begin a transaction, returns the started transaction so that it can be
	// committed or rolled back
	ITransactionCompleter BeginTransaction();
}

// abstraction to allow the components to commit/rollback
public interface ITransactionCompleter {
	void Rollback();
	void Commit(ScavengeCheckpoint checkpoint);
}

public interface ITransactionManager : ITransactionCompleter {
	void Begin();
	void RegisterOnRollback(Action onRollback);
	void UnregisterOnRollback();
}

// abstraction for the backing store. memory, sqlite etc.
public interface ITransactionFactory<TTransaction> {
	TTransaction Begin();
	void Rollback(TTransaction transaction);
	void Commit(TTransaction transaction);
}

public interface IScavengeStateForAccumulator<TStreamId> :
	IScavengeStateCommon,
	IIncreaseChunkWeights {

	// call this for each record as we accumulate through the log so that we can spot every hash
	// collision to save ourselves work later.
	// this affects multiple parts of the scavengestate and must be called within a transaction so
	// that its effect is atomic
	void DetectCollisions(TStreamId streamId);

	void SetMetastreamDiscardPoint(TStreamId metastreamId, DiscardPoint discardPoint);

	// for when the _original_ stream is tombstoned, the metadata stream can be removed entirely.
	void SetMetastreamTombstone(TStreamId metastreamId);

	void SetOriginalStreamMetadata(TStreamId originalStreamId, StreamMetadata metadata);

	void SetOriginalStreamTombstone(TStreamId originalStreamId);

	void SetChunkTimeStampRange(int logicalChunkNumber, ChunkTimeStampRange range);

	StreamHandle<TStreamId> GetStreamHandle(TStreamId streamId);

	void LogAccumulationStats();
}

public interface IScavengeStateForCalculatorReadOnly<TStreamId> {
	// Calculator iterates through the scavengable original streams and their metadata
	// it doesn't need to do anything with the metadata streams, accumulator has done those.
	IEnumerable<(StreamHandle<TStreamId>, OriginalStreamData)> OriginalStreamsToCalculate(
		StreamHandle<TStreamId> checkpoint);

	bool TryGetChunkTimeStampRange(int logicaChunkNumber, out ChunkTimeStampRange range);
}

public interface IScavengeStateForCalculator<TStreamId> :
	IScavengeStateForCalculatorReadOnly<TStreamId>,
	IScavengeStateCommon,
	IIncreaseChunkWeights {

	void SetOriginalStreamDiscardPoints(
		StreamHandle<TStreamId> streamHandle,
		CalculationStatus status,
		DiscardPoint discardPoint,
		DiscardPoint maybeDiscardPoint);

	TStreamId LookupUniqueHashUser(ulong streamHash);
}

public interface IIncreaseChunkWeights {
	void IncreaseChunkWeight(int logicalChunkNumber, float extraWeight);
}

public interface IScavengeStateForChunkExecutor<TStreamId> : IScavengeStateCommon {
	IScavengeStateForChunkExecutorWorker<TStreamId> BorrowStateForWorker();
}

// implementations must be thread-safe
public interface IScavengeStateForChunkExecutorWorker<TStreamId> : IDisposable {
	void ResetChunkWeights(int startLogicalChunkNumber, int endLogicalChunkNumber);
	float SumChunkWeights(int startLogicalChunkNumber, int endLogicalChunkNumber);
	bool TryGetChunkExecutionInfo(TStreamId streamId, out ChunkExecutionInfo info);
	bool TryGetMetastreamData(TStreamId streamId, out MetastreamData metastreamData);
	bool TryGetChunkTimeStampRange(int logicalChunkNumber, out ChunkTimeStampRange range);
}

public interface IScavengeStateForChunkMerger : IScavengeStateCommon {
}

public interface IScavengeStateForIndexExecutor<TStreamId> : IScavengeStateCommon {
	bool IsCollision(ulong streamHash);
	bool TryGetIndexExecutionInfo(
		StreamHandle<TStreamId> streamHandle,
		out IndexExecutionInfo info);
	TStreamId LookupUniqueHashUser(ulong streamHash);
}

public interface IScavengeStateForCleaner : IScavengeStateCommon {
	bool AllChunksExecuted();
	void DeleteMetastreamData();
	void DeleteOriginalStreamData(bool deleteArchived);
}

// this represents access to the actual state storage. these are grouped together into one inteface
// so that they can be accessed transactionally
public interface IScavengeStateBackend<TStreamId> : IDisposable {
	void LogStats();
	IScavengeMap<TStreamId, Unit> CollisionStorage { get; }
	IScavengeMap<ulong, TStreamId> Hashes { get; }
	IMetastreamScavengeMap<ulong> MetaStorage { get; }
	IMetastreamScavengeMap<TStreamId> MetaCollisionStorage { get; }
	IOriginalStreamScavengeMap<ulong> OriginalStorage { get; }
	IOriginalStreamScavengeMap<TStreamId> OriginalCollisionStorage { get; }
	IScavengeMap<Unit, ScavengeCheckpoint> CheckpointStorage { get; }
	IScavengeMap<int, ChunkTimeStampRange> ChunkTimeStampRanges { get; }
	IChunkWeightScavengeMap ChunkWeights { get; }
	ITransactionManager TransactionManager { get; }
}
