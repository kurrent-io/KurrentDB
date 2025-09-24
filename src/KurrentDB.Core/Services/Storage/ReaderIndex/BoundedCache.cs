// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Common.Utils;

namespace KurrentDB.Core.Services.Storage.ReaderIndex;

public class BoundedCache<TKey, TValue> {
	private readonly int _maxCachedEntries;
	private readonly long _maxDataSize;
	private readonly Func<TValue, long> _valueSize;
	private readonly Dictionary<TKey, TValue> _cache = [];
	private readonly Queue<TKey> _queue = new();

	private long _currentSize;

	public BoundedCache(int maxCachedEntries, long maxDataSize, Func<TValue, long> valueSize) {
		_maxCachedEntries = Ensure.Positive(maxCachedEntries);
		_maxDataSize = Ensure.Positive(maxDataSize);
		_valueSize = Ensure.NotNull(valueSize);
	}

	public bool TryGetRecord(TKey key, out TValue value) {
		var found = _cache.TryGetValue(key, out value);
		if (found) {
		} else {
		}

		return found;
	}

	public void PutRecord(TKey key, TValue value, bool throwOnDuplicate) {
		while (IsFull()) {
			var oldKey = _queue.Dequeue();
			RemoveRecord(oldKey);
		}

		_currentSize += _valueSize(value);
		_queue.Enqueue(key);
		if (!throwOnDuplicate && _cache.ContainsKey(key))
			return;
		_cache.Add(key, value);
	}

	private bool IsFull() => _queue.Count >= _maxCachedEntries || (_currentSize > _maxDataSize && _queue.Count > 0);

	private void RemoveRecord(TKey key) {
		if (_cache.TryGetValue(key, out var old)) {
			_currentSize -= _valueSize(old);
			_cache.Remove(key);
		}
	}
}
