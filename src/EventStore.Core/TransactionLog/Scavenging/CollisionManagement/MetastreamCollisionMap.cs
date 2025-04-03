// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Index.Hashes;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.TransactionLog.Scavenging.CollisionManagement;

public class MetastreamCollisionMap<TStreamId> : CollisionMap<TStreamId, MetastreamData> {

	private readonly ILongHasher<TStreamId> _hasher;
	private readonly Func<TStreamId, bool> _isCollision;
	private readonly IMetastreamScavengeMap<ulong> _nonCollisions;
	private readonly IMetastreamScavengeMap<TStreamId> _collisions;

	public MetastreamCollisionMap(
		ILongHasher<TStreamId> hasher,
		Func<TStreamId, bool> isCollision,
		IMetastreamScavengeMap<ulong> nonCollisions,
		IMetastreamScavengeMap<TStreamId> collisions) :
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

	public void SetDiscardPoint(TStreamId streamId, DiscardPoint discardPoint) {
		if (_isCollision(streamId))
			_collisions.SetDiscardPoint(streamId, discardPoint);
		else
			_nonCollisions.SetDiscardPoint(_hasher.Hash(streamId), discardPoint);
	}

	public void DeleteAll() {
		_collisions.DeleteAll();
		_nonCollisions.DeleteAll();
	}
}
