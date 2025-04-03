// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Index.Hashes;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.TransactionLog.Scavenging.CollisionManagement;

// this class efficiently stores/retrieves data against keys that very rarely but sometimes have a
// hash collision.
// when there is a hash collision the key is stored explicitly with the value
// otherwise it only stores the hashes and the values.
//
// in practice this allows us to
//   1. store data for lots of streams with much reduced size and complexity
//      because we rarely if ever need to store the stream name, and the hashes are fixed size
//   2. when being read, inform the caller whether the key hash collides or not
//
// for retrieval, if you have the key (and it has been checked for collision) then this
// will look in the right submap. if you have a handle then this will look into the submap
// according to the kind of handle.
public class CollisionMap<TKey, TValue> {
	private readonly IScavengeMap<ulong, TValue> _nonCollisions;
	private readonly IScavengeMap<TKey, TValue> _collisions;
	private readonly ILongHasher<TKey> _hasher;
	private readonly Func<TKey, bool> _isCollision;

	public CollisionMap(
		ILongHasher<TKey> hasher,
		Func<TKey, bool> isCollision,
		IScavengeMap<ulong, TValue> nonCollisions,
		IScavengeMap<TKey, TValue> collisions) {

		_hasher = hasher;
		_isCollision = isCollision;
		_nonCollisions = nonCollisions;
		_collisions = collisions;
	}

	// the key must already be checked for collisions so that we know if it _isCollision
	// (especially, be careful when converting a stream to its metastream or vice versa)
	public bool TryGetValue(TKey key, out TValue value) =>
		_isCollision(key)
			? _collisions.TryGetValue(key, out value)
			: _nonCollisions.TryGetValue(_hasher.Hash(key), out value);

	public bool TryGetValue(StreamHandle<TKey> handle, out TValue value) {
		switch (handle.Kind) {
			case StreamHandle.Kind.Hash:
				return _nonCollisions.TryGetValue(handle.StreamHash, out value);
			case StreamHandle.Kind.Id:
				return _collisions.TryGetValue(handle.StreamId, out value);
			default:
				throw new ArgumentOutOfRangeException(nameof(handle), handle, null);
		}
	}

	public TValue this[TKey key] {
		set {
			if (_isCollision(key)) {
				_collisions[key] = value;
			} else {
				_nonCollisions[_hasher.Hash(key)] = value;
			}
		}
	}

	// when a key that didn't used to be a collision, becomes a collision.
	// the remove and the add must be performed atomically.
	// but the overall operation is idempotent
	public void NotifyCollision(TKey key) {
		var hash = _hasher.Hash(key);
		if (_nonCollisions.TryRemove(hash, out var value)) {
			_collisions[key] = value;
		} else {
			// we are notified that the key is a collision, but we dont have any entry for it
			// so nothing to do
		}
	}
}
