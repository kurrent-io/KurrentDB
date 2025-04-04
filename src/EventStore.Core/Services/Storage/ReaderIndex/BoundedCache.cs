using System;
using System.Collections.Generic;
using System.Threading;
using EventStore.Common.Utils;

namespace EventStore.Core.Services.Storage.ReaderIndex {
	public class BoundedCache<TKey, TValue> {
		public int Count {
			get { return _cache.Count; }
		}

		private readonly int _maxCachedEntries;
		private readonly long _maxDataSize;
		private readonly Func<TValue, long> _valueSize;

		private readonly Dictionary<TKey, TValue> _cache = new Dictionary<TKey, TValue>();
		private readonly Queue<TKey> _queue = new Queue<TKey>();

		private long _currentSize;
		private long _missCount;
		private long _hitCount;

		public BoundedCache(int maxCachedEntries, long maxDataSize, Func<TValue, long> valueSize) {
			Ensure.NotNull(valueSize, "valueSize");
			if (maxCachedEntries <= 0)
				throw new ArgumentOutOfRangeException("maxCachedEntries");
			if (maxDataSize <= 0)
				throw new ArgumentOutOfRangeException("maxDataSize");

			_maxCachedEntries = maxCachedEntries;
			_maxDataSize = maxDataSize;
			_valueSize = valueSize;
		}

		public bool TryGetRecord(TKey key, out TValue value) {
			var found = _cache.TryGetValue(key, out value);
			if (found)
				_hitCount++;
			else
				_missCount++;
			return found;
		}

		public void PutRecord(TKey key, TValue value) {
			PutRecord(key, value, true);
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

		public bool TryPutRecord(TKey key, TValue value) {
			if (IsFull())
				return false;

			_currentSize += _valueSize(value);
			_queue.Enqueue(key);
			_cache.Add(key, value); // add to throw exception if duplicate
			return true;
		}

		public void Clear() {
			_currentSize = 0;
			_queue.Clear();
			_cache.Clear();
		}

		private bool IsFull() {
			return _queue.Count >= _maxCachedEntries
				   || (_currentSize > _maxDataSize && _queue.Count > 0);
		}

		public void RemoveRecord(TKey key) {
			TValue old;
			if (_cache.TryGetValue(key, out old)) {
				_currentSize -= _valueSize(old);
				_cache.Remove(key);
			}
		}

		public ReadCacheStats GetStatistics() {
			return new ReadCacheStats(Interlocked.Read(ref _currentSize),
				_cache.Count,
				Interlocked.Read(ref _hitCount),
				Interlocked.Read(ref _missCount));
		}
	}
}
