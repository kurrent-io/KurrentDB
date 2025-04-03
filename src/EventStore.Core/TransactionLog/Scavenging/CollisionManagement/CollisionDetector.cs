// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Index.Hashes;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Serilog;

namespace EventStore.Core.TransactionLog.Scavenging.CollisionManagement;

// add things to the collision detector and it keeps a list of things that collided.
public class CollisionDetector<T> {
	private static EqualityComparer<T> TComparer { get; } = EqualityComparer<T>.Default;

	private readonly ILogger _logger;

	// maps from hash to (any) user of that hash
	private readonly IScavengeMap<ulong, T> _hashUsers;
	private readonly ILongHasher<T> _hasher;

	// store the values. could possibly store just the hashes instead but that would
	// lose us information and it should be so rare that there are any collisions at all.
	private readonly IScavengeMap<T, Unit> _collisions;

	// the entire set of collisions, or null if invalidated
	private Dictionary<T, Unit> _collisionsCache;
	private Dictionary<ulong, Unit> _collisionsHashCache;

	private long _newStreams;
	private long _newCollisions;
	private long _oldCollisions;

	public CollisionDetector(
		ILogger logger,
		IScavengeMap<ulong, T> hashUsers,
		IScavengeMap<T, Unit> collisionStorage,
		ILongHasher<T> hasher) {
		_logger = logger;
		_hashUsers = hashUsers;
		_collisions = collisionStorage;
		_hasher = hasher;
	}

	public void LogStats() {
		long hits = 0;
		long misses = 0;

		if (_hashUsers is LruCachingScavengeMap<ulong, T> lru)
			lru.GetStats(out hits, out misses);

		_logger.Debug(
			"SCAVENGING: CollisionDetector stats: " +
			"{newStreams:N0} new streams. {newCollisions:N0} new collisions. {oldCollisions:N0} old collisions. " +
			"{hits:N0} hits. {misses:N0} misses.",
			_newStreams, _newCollisions, _oldCollisions,
			hits, misses);

		_newStreams = 0;
		_newCollisions = 0;
		_oldCollisions = 0;
	}

	public void ClearCaches() {
		_collisionsCache = null;
		_collisionsHashCache = null;
	}

	public bool IsCollision(T item) {
		EnsureCollisionsCache();
		return _collisionsCache.TryGetValue(item, out _);
	}

	public bool IsCollisionHash(ulong hash) {
		if (_collisionsHashCache == null) {
			_collisionsHashCache = new Dictionary<ulong, Unit>();
			foreach (var kvp in _collisions.AllRecords()) {
				_collisionsHashCache[_hasher.Hash(kvp.Key)] = kvp.Value;
			}
		}
		return _collisionsHashCache.TryGetValue(hash, out _);
	}

	public IEnumerable<T> AllCollisions() {
		EnsureCollisionsCache();
		return _collisionsCache.Keys;
	}

	private void EnsureCollisionsCache() {
		if (_collisionsCache == null)
			_collisionsCache = _collisions.AllRecords().ToDictionary(
				x => x.Key,
				x => x.Value);
	}

	// Proof by induction that DetectCollisions works
	// either:
	//   1. we have seen this stream before.
	//       then either:
	//       a. it was a collision the first time we saw it
	//           - by induction (2a) we concluded it was a collision at that time and added it to the
	//             collision list so we conclude it is a collision now
	//             TICK
	//       b. it was not a collision but has since been collided with by a newer stream.
	//           - when it was collided (2a) with we noticed and added it to the collision list so
	//             we conclude it is still a collision now
	//             TICK
	//       c. it was not a collision and still isn't
	//           - so we look in the backing data to see if this hash is in use
	//             it is, because we have seen this stream before, but since this is not a collision
	//             the user of the stream turns out to be us.
	//             conclude no collision
	//             TICK
	//
	//   2. this is the first time we are seeing this stream.
	//       then either:
	//       a. it collides with any stream we have seen before
	//           - so we look in the backing data to see if this hash is in use.
	//             it is, because this is the colliding case.
	//             we look up the record that uses this hash
	//             the record MUST be for a different stream because this is the the first time
	//                we are seeing this stream
	//             collision detected.
	//             add both streams to the collision list. we have their names and the hash.
	//             NB it is possible that we are colliding with multiple streams,
	//                    but they are already in the backing data
	//                    because they are colliding with each other.
	//             TICK
	//       b. it does not collide with any stream we have seen before
	//           - so we look in the backing data to see if there are any entries for this hash
	//             there are not because this is the first time we are seeing this stream and it does
	//                not collde.
	//             conclude no collision, register this stream as the user.
	//             TICK

	// Adds an item and detects if it collides with other items that were already added.
	// `collision` is only defined when returning NewCollision.
	// in this way we can tell when anything that was not colliding becomes colliding.
	public CollisionResult DetectCollisions(T item, out T collision) {
		if (IsCollision(item)) {
			collision = default;
			_oldCollisions++;
			return CollisionResult.OldCollision; // previously known collision. 1a or 1b.
			// _hashes must already have this hash, otherwise it wouldn't be a collision.
			// so no need to add it it.
		}

		// collision not previously known, but might be a new one now.
		var itemHash = _hasher.Hash(item);
		if (!_hashUsers.TryGetValue(itemHash, out collision)) {
			// hash wasn't even in use. it is now.
			_hashUsers[itemHash] = item;
			_newStreams++;
			return CollisionResult.NoCollision; // hash not in use, can be no collision. 2b
		}

		// hash in use, but maybe by the item itself.
		if (TComparer.Equals(collision, item)) {
			return CollisionResult.NoCollision; // no collision with oneself. 1c
		}

		// hash in use by a different item! found new collision. 2a
		_collisions[item] = Unit.Instance;
		_collisions[collision] = Unit.Instance;
		ClearCaches();

		_newCollisions++;
		return CollisionResult.NewCollision;
	}

	public T LookupUniqueHashUser(ulong streamHash) {
		if (!_hashUsers.TryGetValue(streamHash, out var stream))
			throw new Exception($"Tried to get unique user for stream hash: {streamHash} but it is unused");

		if (IsCollisionHash(streamHash)) {
			var collisions = AllCollisions().Where(x => _hasher.Hash(x) == streamHash);
			throw new Exception($"Tried to get unique user for stream hash: {streamHash} but there are multiple: {string.Join(",", collisions)}");
		}

		return stream;
	}
}
