// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.DataStructures;
using EventStore.Core.Index.Hashes;
using EventStore.Core.LogAbstraction;
using EventStore.Core.TransactionLog.Scavenging.CollisionManagement;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Serilog;

namespace EventStore.Core.TransactionLog.Scavenging;

// This datastructure is read and written to by the Accumulator/Calculator/Executors.
// They contain the scavenge logic, this is just the holder of the data.
//
// we store data for metadata streams and for original streams, but we need to store
// different data for each so we have two maps. we have one collision detector since
// we need to detect collisions between all of the streams.
// we don't need to store data for every original stream, only ones that need scavenging.
public class ScavengeState<TStreamId> : IScavengeState<TStreamId> {
	private bool _initialized;
	private IScavengeStateBackend<TStreamId> _backend;
	private readonly ObjectPool<IScavengeStateBackend<TStreamId>> _backendPool;
	private readonly int _hashUsersCacheCapacity;
	private CollisionDetector<TStreamId> _collisionDetector;

	// data stored keyed against metadata streams
	private MetastreamCollisionMap<TStreamId> _metastreamDatas;

	// data stored keyed against original (non-metadata) streams
	private OriginalStreamCollisionMap<TStreamId> _originalStreamDatas;

	private IScavengeMap<int, ChunkTimeStampRange> _chunkTimeStampRanges;
	private IChunkWeightScavengeMap _chunkWeights;
	private IScavengeMap<Unit, ScavengeCheckpoint> _checkpointStorage;
	private ITransactionManager _transactionManager;

	private readonly ILogger _logger;
	private readonly ILongHasher<TStreamId> _hasher;
	private readonly IMetastreamLookup<TStreamId> _metastreamLookup;

	public ScavengeState(
		ILogger logger,
		ILongHasher<TStreamId> hasher,
		IMetastreamLookup<TStreamId> metastreamLookup,
		ObjectPool<IScavengeStateBackend<TStreamId>> backendPool,
		int hashUsersCacheCapacity) {

		_logger = logger;
		_hasher = hasher;
		_metastreamLookup = metastreamLookup;
		_backendPool = backendPool;
		_hashUsersCacheCapacity = hashUsersCacheCapacity;
	}

	public void Init() {
		if (_initialized)
			return;

		_initialized = true;
		_backend = _backendPool.Get();

		// todo: in log v3 inject an implementation that doesn't store hash users
		// since there are no collisions.
		_collisionDetector = new CollisionDetector<TStreamId>(
			logger: _logger,
			hashUsers: new LruCachingScavengeMap<ulong, TStreamId>(
				"HashUsers",
				_backend.Hashes,
				cacheMaxCount: _hashUsersCacheCapacity),
			collisionStorage: _backend.CollisionStorage,
			hasher: _hasher);

		_checkpointStorage = _backend.CheckpointStorage;

		_metastreamDatas = new MetastreamCollisionMap<TStreamId>(
			_hasher,
			_collisionDetector.IsCollision,
			_backend.MetaStorage,
			_backend.MetaCollisionStorage);

		_originalStreamDatas = new OriginalStreamCollisionMap<TStreamId>(
			_hasher,
			_collisionDetector.IsCollision,
			_backend.OriginalStorage,
			_backend.OriginalCollisionStorage);

		_chunkTimeStampRanges = _backend.ChunkTimeStampRanges;
		_chunkWeights = _backend.ChunkWeights;

		_transactionManager = _backend.TransactionManager;
		_transactionManager.RegisterOnRollback(OnRollback);
	}

	public void Dispose() {
		_transactionManager?.UnregisterOnRollback();
		if (_backend != null)
			_backendPool.Return(_backend);
		_backendPool.Dispose();
	}

	private void OnRollback() {
		// a transaction has been rolled back
		// need to clear whatever we think we know about collisions to stop us mistaking a new
		// collision for an old one.
		_collisionDetector.ClearCaches();

		// there is no need to clear the HashUsers LRU cache
	}

	public void LogStats() {
		_backend.LogStats();
	}

	// reuses the same transaction object for multiple transactions.
	// caller is reponsible for committing, rolling back, or disposing
	// the transaction before calling BeginTransaction again
	public ITransactionCompleter BeginTransaction() {
		_transactionManager.Begin();
		return _transactionManager;
	}

	public bool TryGetCheckpoint(out ScavengeCheckpoint checkpoint) =>
		_checkpointStorage.TryGetValue(Unit.Instance, out checkpoint);

	public IEnumerable<TStreamId> AllCollisions() {
		return _collisionDetector.AllCollisions();
	}

	public bool TryGetOriginalStreamData(
		TStreamId streamId,
		out OriginalStreamData originalStreamData) =>

		_originalStreamDatas.TryGetValue(streamId, out originalStreamData);

	//
	// FOR ACCUMULATOR
	//

	public void DetectCollisions(TStreamId streamId) {
		var collisionResult = _collisionDetector.DetectCollisions(
			streamId,
			out var collision);

		if (collisionResult == CollisionResult.NewCollision) {
			_logger.Information(
				"SCAVENGING: Detected collision between streams \"{streamId}\" and \"{previous}\"",
				streamId, collision);

			_metastreamDatas.NotifyCollision(collision);
			_originalStreamDatas.NotifyCollision(collision);
		}
	}

	public void SetMetastreamDiscardPoint(TStreamId metastreamId, DiscardPoint discardPoint) {
		_metastreamDatas.SetDiscardPoint(metastreamId, discardPoint);
	}

	public void SetMetastreamTombstone(TStreamId metastreamId) {
		_metastreamDatas.SetTombstone(metastreamId);
	}

	public void SetOriginalStreamMetadata(TStreamId originalStreamId, StreamMetadata metadata) {
		_originalStreamDatas.SetMetadata(originalStreamId, metadata);
	}

	public void SetOriginalStreamTombstone(TStreamId originalStreamId) {
		_originalStreamDatas.SetTombstone(originalStreamId);
	}

	public void SetChunkTimeStampRange(int logicalChunkNumber, ChunkTimeStampRange range) {
		_chunkTimeStampRanges[logicalChunkNumber] = range;
	}

	public StreamHandle<TStreamId> GetStreamHandle(TStreamId streamId) =>
		_collisionDetector.IsCollision(streamId) ?
			StreamHandle.ForStreamId(streamId) :
			StreamHandle.ForHash<TStreamId>(_hasher.Hash(streamId));

	public void LogAccumulationStats() {
		LogStats();
		_collisionDetector.LogStats();
	}

	//
	// FOR CALCULATOR
	//

	public IEnumerable<(StreamHandle<TStreamId>, OriginalStreamData)> OriginalStreamsToCalculate(
		StreamHandle<TStreamId> checkpoint) {

		return _originalStreamDatas.EnumerateActive(checkpoint);
	}

	public void SetOriginalStreamDiscardPoints(
		StreamHandle<TStreamId> handle,
		CalculationStatus status,
		DiscardPoint discardPoint,
		DiscardPoint maybeDiscardPoint) {

		_originalStreamDatas.SetDiscardPoints(handle, status, discardPoint, maybeDiscardPoint);
	}

	public void IncreaseChunkWeight(int logicalChunkNumber, float extraWeight) {
		_chunkWeights.IncreaseWeight(logicalChunkNumber, extraWeight);
	}

	public bool TryGetChunkTimeStampRange(int logicalChunkNumber, out ChunkTimeStampRange range) =>
		_chunkTimeStampRanges.TryGetValue(logicalChunkNumber, out range);

	public TStreamId LookupUniqueHashUser(ulong streamHash) =>
		_collisionDetector.LookupUniqueHashUser(streamHash);

	//
	// FOR CHUNK EXECUTOR
	//

	// not guaranteed to be thread safe. allocations and queries, dont call this too often.
	public IScavengeStateForChunkExecutorWorker<TStreamId> BorrowStateForWorker() {
		var backend = _backendPool.Get();

		var state = new ScavengeStateForChunkWorker<TStreamId>(
			hasher: _hasher,
			backend: backend,
			collisions: AllCollisions().ToDictionary(x => x, x => Unit.Instance),
			onDispose: () => _backendPool.Return(backend));
		return state;
	}

	public float SumChunkWeights(int startLogicalChunkNumber, int endLogicalChunkNumber) =>
		_chunkWeights.SumChunkWeights(startLogicalChunkNumber, endLogicalChunkNumber);

	public void ResetChunkWeights(int startLogicalChunkNumber, int endLogicalChunkNumber) {
		_chunkWeights.ResetChunkWeights(startLogicalChunkNumber, endLogicalChunkNumber);
	}

	public bool TryGetChunkExecutionInfo(
		TStreamId streamId,
		out ChunkExecutionInfo info) =>

		_originalStreamDatas.TryGetChunkExecutionInfo(streamId, out info);

	public bool TryGetMetastreamData(TStreamId streamId, out MetastreamData data) =>
		_metastreamDatas.TryGetValue(streamId, out data);

	//
	// FOR INDEX EXECUTOR
	//

	public bool TryGetIndexExecutionInfo(
		StreamHandle<TStreamId> handle,
		out IndexExecutionInfo info) {

		// here we know that the handle is of the correct kind
		// but we do not know whether it is for a metastream or an originalstream.
		switch (handle.Kind) {
			case StreamHandle.Kind.Hash:
				// not a collision, but we do not know whether it is a metastream or not.
				// check both maps (better if we didnt have to though..)
				return TryGetDiscardPointForOriginalStream(handle, out info)
					|| TryGetDiscardPointForMetadataStream(handle, out info);
			case StreamHandle.Kind.Id:
				// collision, but at least we can tell whether it is a metastream or not.
				// so just check one map.
				return _metastreamLookup.IsMetaStream(handle.StreamId)
					? TryGetDiscardPointForMetadataStream(handle, out info)
					: TryGetDiscardPointForOriginalStream(handle, out info);
			default:
				throw new ArgumentOutOfRangeException(nameof(handle), handle, null);
		}
	}

	private bool TryGetDiscardPointForMetadataStream(
		StreamHandle<TStreamId> handle,
		out IndexExecutionInfo info) {

		if (!_metastreamDatas.TryGetValue(handle, out var data)) {
			info = default;
			return false;
		}

		info = new IndexExecutionInfo(
			isMetastream: true,
			isTombstoned: data.IsTombstoned,
			discardPoint: data.DiscardPoint);
		return true;
	}

	private bool TryGetDiscardPointForOriginalStream(
		StreamHandle<TStreamId> handle,
		out IndexExecutionInfo info) {

		if (!_originalStreamDatas.TryGetValue(handle, out var data)) {
			info = default;
			return false;
		}

		info = new IndexExecutionInfo(
			isMetastream: false,
			isTombstoned: data.IsTombstoned,
			discardPoint: data.DiscardPoint);
		return true;
	}

	public bool IsCollision(ulong streamHash) {
		return _collisionDetector.IsCollisionHash(streamHash);
	}

	//
	// For cleaner
	//

	public bool AllChunksExecuted() =>
		_chunkWeights.AllWeightsAreZero();

	public void DeleteOriginalStreamData(bool deleteArchived) {
		_originalStreamDatas.DeleteMany(deleteArchived: deleteArchived);
	}

	public void DeleteMetastreamData() {
		_metastreamDatas.DeleteAll();
	}
}


// in the chunk executor each worker gets its own state so that it has its own dbconnection and
// prepared commands.
public readonly struct ScavengeStateForChunkWorker<TStreamId> : 
	IScavengeStateForChunkExecutorWorker<TStreamId>{

	private readonly MetastreamCollisionMap<TStreamId> _metastreamDatas;
	private readonly OriginalStreamCollisionMap<TStreamId> _originalStreamDatas;
	private readonly IScavengeMap<int, ChunkTimeStampRange> _chunkTimeStampRanges;
	private readonly IChunkWeightScavengeMap _chunkWeights;
	private readonly Action _onDispose;

	public ScavengeStateForChunkWorker(
		ILongHasher<TStreamId> hasher,
		IScavengeStateBackend<TStreamId> backend,
		Dictionary<TStreamId, Unit> collisions,
		Action onDispose) {

		_metastreamDatas = new MetastreamCollisionMap<TStreamId>(
			hasher,
			collisions.ContainsKey,
			backend.MetaStorage,
			backend.MetaCollisionStorage);

		_originalStreamDatas = new OriginalStreamCollisionMap<TStreamId>(
			hasher,
			collisions.ContainsKey,
			backend.OriginalStorage,
			backend.OriginalCollisionStorage);

		_chunkTimeStampRanges = backend.ChunkTimeStampRanges;
		_chunkWeights = backend.ChunkWeights;

		_onDispose = onDispose;
	}

	public float SumChunkWeights(int startLogicalChunkNumber, int endLogicalChunkNumber) =>
		_chunkWeights.SumChunkWeights(startLogicalChunkNumber, endLogicalChunkNumber);

	public void ResetChunkWeights(int startLogicalChunkNumber, int endLogicalChunkNumber) =>
		_chunkWeights.ResetChunkWeights(startLogicalChunkNumber, endLogicalChunkNumber);

	public bool TryGetChunkExecutionInfo(TStreamId streamId, out ChunkExecutionInfo info) =>
		_originalStreamDatas.TryGetChunkExecutionInfo(streamId, out info);

	public bool TryGetMetastreamData(TStreamId streamId, out MetastreamData data) =>
		_metastreamDatas.TryGetValue(streamId, out data);

	public bool TryGetChunkTimeStampRange(int logicalChunkNumber, out ChunkTimeStampRange range) =>
		_chunkTimeStampRanges.TryGetValue(logicalChunkNumber, out range);

	public void Dispose() {
		_onDispose();
	}
}
