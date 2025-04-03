// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.DataStructures;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.TransactionLog.Scavenging;

// All access to the wrapped map must be via the cache.
// Currently this is only used to cache the hash users. See comments below.
public class LruCachingScavengeMap<TKey, TValue> : IScavengeMap<TKey, TValue> {
	private readonly LRUCache<TKey, TValue> _cache;
	private readonly IScavengeMap<TKey, TValue> _wrapped;
	private long _hits;
	private long _misses;

	public LruCachingScavengeMap(string name, IScavengeMap<TKey, TValue> wrapped, int cacheMaxCount) {
		_wrapped = wrapped;
		_cache = new LRUCache<TKey, TValue>(name, cacheMaxCount);
	}

	public TValue this[TKey key] {
		set {
			_wrapped[key] = value;
			_cache.Put(key, value);
		}
	}

	public IEnumerable<KeyValuePair<TKey, TValue>> AllRecords() =>
		_wrapped.AllRecords();

	public bool TryGetValue(TKey key, out TValue value) {
		if (_cache.TryGet(key, out value)) {
			_hits++;
			return true;
		}

		_misses++;

		if (_wrapped.TryGetValue(key, out value)) {
			_cache.Put(key, value);
			return true;
		}

		// Currently this is only used to cache the hash users. As such if we TryGetValue and fail to
		// find it then we will always be adding a value for that key immediately, so it is not
		// useful to remember keys that are known to not exist.
		return false;
	}

	public bool TryRemove(TKey key, out TValue value) {
		_cache.Remove(key);
		return _wrapped.TryRemove(key, out value);
	}

	public void GetStats(out long hits, out long misses) {
		hits = _hits;
		misses = _misses;
		_hits = 0;
		_misses = 0;
	}
}
