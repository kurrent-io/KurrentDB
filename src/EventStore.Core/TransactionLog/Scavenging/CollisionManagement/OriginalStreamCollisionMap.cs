// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Index.Hashes;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.TransactionLog.Scavenging.CollisionManagement;

public class OriginalStreamCollisionMap<TStreamId> :
	CollisionMap<TStreamId, OriginalStreamData> {

	private readonly ILongHasher<TStreamId> _hasher;
	private readonly Func<TStreamId, bool> _isCollision;
	private readonly IOriginalStreamScavengeMap<ulong> _nonCollisions;
	private readonly IOriginalStreamScavengeMap<TStreamId> _collisions;

	public OriginalStreamCollisionMap(
		ILongHasher<TStreamId> hasher,
		Func<TStreamId, bool> isCollision,
		IOriginalStreamScavengeMap<ulong> nonCollisions,
		IOriginalStreamScavengeMap<TStreamId> collisions) :
		base(
			hasher, isCollision, nonCollisions, collisions) {

		_hasher = hasher;
		_isCollision = isCollision;
		_nonCollisions = nonCollisions;
		_collisions = collisions;
	}

	public void SetTombstone(TStreamId streamId) {
		if (_isCollision(streamId))
			_collisions.SetTombstone(streamId);
		else
			_nonCollisions.SetTombstone(_hasher.Hash(streamId));
	}

	public void SetMetadata(TStreamId streamId, StreamMetadata metadata) {
		if (_isCollision(streamId))
			_collisions.SetMetadata(streamId, metadata);
		else
			_nonCollisions.SetMetadata(_hasher.Hash(streamId), metadata);
	}

	public void SetDiscardPoints(
		StreamHandle<TStreamId> handle,
		CalculationStatus status,
		DiscardPoint discardPoint,
		DiscardPoint maybeDiscardPoint) {

		switch (handle.Kind) {
			case StreamHandle.Kind.Hash:
				_nonCollisions.SetDiscardPoints(
					key: handle.StreamHash,
					status: status,
					discardPoint: discardPoint,
					maybeDiscardPoint: maybeDiscardPoint);
				break;
			case StreamHandle.Kind.Id:
				_collisions.SetDiscardPoints(
					key: handle.StreamId,
					status: status,
					discardPoint: discardPoint,
					maybeDiscardPoint: maybeDiscardPoint);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(handle), handle, null);
		}
	}

	public bool TryGetChunkExecutionInfo(TStreamId streamId, out ChunkExecutionInfo info) =>
		_isCollision(streamId)
			? _collisions.TryGetChunkExecutionInfo(streamId, out info)
			: _nonCollisions.TryGetChunkExecutionInfo(_hasher.Hash(streamId), out info);

	public void DeleteMany(bool deleteArchived) {
		_collisions.DeleteMany(deleteArchived: deleteArchived);
		_nonCollisions.DeleteMany(deleteArchived: deleteArchived);
	}

	// overall sequence is collisions ++ noncollisions
	public IEnumerable<(StreamHandle<TStreamId>, OriginalStreamData)> EnumerateActive(
		StreamHandle<TStreamId> checkpoint) {

		IEnumerable<KeyValuePair<TStreamId, OriginalStreamData>> collisionsEnumerable;
		IEnumerable<KeyValuePair<ulong, OriginalStreamData>> nonCollisionsEnumerable;

		switch (checkpoint.Kind) {
			case StreamHandle.Kind.None:
				// no checkpoint, emit everything
				collisionsEnumerable = _collisions.ActiveRecords();
				nonCollisionsEnumerable = _nonCollisions.ActiveRecords();
				break;

			case StreamHandle.Kind.Id:
				// checkpointed in the collisions. emit the rest of those, then the non-collisions
				collisionsEnumerable = _collisions.ActiveRecordsFromCheckpoint(checkpoint.StreamId);
				nonCollisionsEnumerable = _nonCollisions.ActiveRecords();
				break;

			case StreamHandle.Kind.Hash:
				// checkpointed in the noncollisions. emit the rest of those
				collisionsEnumerable = Enumerable.Empty<KeyValuePair<TStreamId, OriginalStreamData>>();
				nonCollisionsEnumerable = _nonCollisions.ActiveRecordsFromCheckpoint(checkpoint.StreamHash);
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(checkpoint), checkpoint.Kind, null);
		}

		foreach (var kvp in collisionsEnumerable) {
			yield return (StreamHandle.ForStreamId(kvp.Key), kvp.Value);
		}

		foreach (var kvp in nonCollisionsEnumerable) {
			yield return (StreamHandle.ForHash<TStreamId>(kvp.Key), kvp.Value);
		}
	}
}
