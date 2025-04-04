// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Caching;
using Serilog;

namespace EventStore.Core.DataStructures;

public class LRUCache {
	protected static readonly ILogger Log = Serilog.Log.ForContext<LRUCache>();
}

public class LRUCache<TKey, TValue> : LRUCache, ILRUCache<TKey, TValue> {
	private class LRUItem {
		public TKey Key;
		public TValue Value;

		// calculation excludes the size of refs of
		// the key & value to avoid double counting
		public static int Size =>
			MemSizer.ObjectHeaderSize +
			(
				Unsafe.SizeOf<TKey>() +
				Unsafe.SizeOf<TValue>()
			).RoundUpToMultipleOf(IntPtr.Size);
	}

	public delegate int CalculateItemSize(TKey key, TValue value);
	public delegate int CalculateFreedSize(TKey key, TValue value, bool keyFreed, bool valueFreed, bool nodeFreed);

	private readonly LinkedList<LRUItem> _orderList = new();
	private readonly Dictionary<TKey, LinkedListNode<LRUItem>> _items = new();
	private readonly Queue<LinkedListNode<LRUItem>> _nodesPool = new();
	private readonly object _lock = new();
	private readonly string _unit;
	private readonly CalculateItemSize _calculateItemSize;
	private readonly CalculateFreedSize _calculateFreedSize;

	private long _capacity;
	private long _size;
	private long _freedSize;

	private static readonly CalculateItemSize _unitSize = delegate { return 1; };
	private static readonly CalculateFreedSize _zeroSize = delegate { return 0; };

	public string Name { get; }
	public long Size => Interlocked.Read(ref _size);
	public long FreedSize => Interlocked.Read(ref _freedSize);
	public long Capacity => Interlocked.Read(ref _capacity);
	public long Count {
		get {
			lock (_lock) {
				return _items.Count;
			}
		}
	}

	public LRUCache(
		string name,
		long capacity,
		CalculateItemSize calculateItemSize = null,
		CalculateFreedSize calculateFreedSize = null,
		string unit = null) {

		Ensure.NotNull(name, nameof(name));
		Ensure.Nonnegative(capacity, nameof(capacity));
		Name = name;
		_capacity = capacity;
		_size = 0L;
		_calculateItemSize = calculateItemSize ?? _unitSize;
		_calculateFreedSize = calculateFreedSize ?? _zeroSize;
		_unit = unit ?? "items";
	}

	public static int ApproximateItemSize(int keyRefsSize, int valueRefsSize) =>
		LRUItem.Size +
		keyRefsSize +
		valueRefsSize +
		MemSizer.SizeOfLinkedListNode<LRUItem>() + // linked list node
		MemSizer.SizeOfDictionaryEntry<TKey, LinkedListNode<LRUItem>>() + // _items entry
		MemSizer.LinkedListEntrySize; // _orderList entry

	private void PutItem(TKey key, TValue value) {
		lock (_lock) {
			var node = GetNode();
			node.Value.Key = key;
			node.Value.Value = value;

			var itemSize = _calculateItemSize(key, value);
			EnsureCapacity(itemSize, true, out _, out _);

			_items.Add(key, node);
			_orderList.AddLast(node);
			_size += itemSize;
		}
	}

	private void UpdateItem(LinkedListNode<LRUItem> node, TValue value) {
		lock (_lock) {
			const bool reuseNode = true;
			_size -= _calculateItemSize(node.Value.Key, node.Value.Value);
			_size += _calculateItemSize(node.Value.Key, value);
			_freedSize += _calculateFreedSize(node.Value.Key, node.Value.Value, false, true, !reuseNode);

			node.Value.Value = value;

			if (!ReferenceEquals(node, _orderList.Last)) {
				_orderList.Remove(node);
				_orderList.AddLast(node);
			}

			if (_size > _capacity)
				EnsureCapacity(0, reuseNode, out _, out _);
		}
	}

	private void RemoveItem(TKey key) {
		lock (_lock) {
			LinkedListNode<LRUItem> node;
			if (_items.TryGetValue(key, out node)) {
				_orderList.Remove(node);
				_items.Remove(key);
				_size -= _calculateItemSize(key, node.Value.Value);
				_freedSize += _calculateFreedSize(node.Value.Key, node.Value.Value, true, true, false);

				ReturnNode(node);
			}
		}
	}

	private void RemoveFirstItem(bool reuseNode) {
		lock (_lock) {
			var node = _orderList.First;
			if (node == null)
				return;

			_orderList.Remove(node);
			_items.Remove(node.Value.Key);
			_size -= _calculateItemSize(node.Value.Key, node.Value.Value);
			_freedSize += _calculateFreedSize(node.Value.Key, node.Value.Value, true, true, !reuseNode);

			if (reuseNode)
				ReturnNode(node);
		}
	}

	public bool TryGet(TKey key, out TValue value) {
		lock (_lock) {
			LinkedListNode<LRUItem> node;
			if (_items.TryGetValue(key, out node)) {
				_orderList.Remove(node);
				_orderList.AddLast(node);
				value = node.Value.Value;
				return true;
			}

			value = default;
			return false;
		}
	}

	public TValue Put(TKey key, TValue value) {
		lock (_lock) {
			LinkedListNode<LRUItem> node;
			if (!_items.TryGetValue(key, out node))
				PutItem(key, value);
			else
				UpdateItem(node, value);

			return value;
		}
	}

	public void Remove(TKey key) {
		lock (_lock) {
			RemoveItem(key);
		}
	}

	public void Clear() {
		lock (_lock) {
			while (_orderList.Count > 0)
				RemoveFirstItem(false);
		}
	}

	public TValue Put<T>(TKey key, T userData, Func<TKey, T, TValue> addFactory,
		Func<TKey, TValue, T, TValue> updateFactory) {
		lock (_lock) {
			TValue value;
			LinkedListNode<LRUItem> node;
			if (!_items.TryGetValue(key, out node)) {
				value = addFactory(key, userData);
				PutItem(key, value);
			} else {
				value = updateFactory(key, node.Value.Value, userData);
				UpdateItem(node, value);
			}

			return value;
		}
	}

	private void EnsureCapacity(int forItemSize, bool reuseNodes, out int removedCount, out long removedSize) {
		lock (_lock) {
			var initialCount = _items.Count;
			var initialSize = _size;

			while (_items.Count > 0 && _size + forItemSize > _capacity)
				RemoveFirstItem(reuseNodes);

			removedCount = initialCount - _items.Count;
			removedSize = initialSize - _size;
		}
	}

	public void SetCapacity(long newCapacity) {
		const int resizeBatchSize = 100_000;

		if (newCapacity < 0)
			throw new ArgumentOutOfRangeException(nameof(newCapacity));

		var removedCount = 0;
		var removedSize = 0L;

		// when decreasing the capacity, remove items batch by batch to prevent
		// other threads from starving when trying to access the cache.
		// when increasing, jump straight up.
		var curCapacity = Interlocked.Read(ref _capacity);

		while (curCapacity != newCapacity) {
			curCapacity = Math.Max(curCapacity - resizeBatchSize, newCapacity);
			Interlocked.Exchange(ref _capacity, curCapacity);
			EnsureCapacity(0, false, out var curRemovedCount, out var curRemovedSize);
			removedCount += curRemovedCount;
			removedSize += curRemovedSize;
		}

		if (removedCount > 0)
			Log.Information(
				"{name} cache removed {removedCount:N0} entries amounting to ~{removedSize:N0} " + _unit,
				Name, removedCount, removedSize);
	}

	public void ResetFreedSize() {
		// note: if something's already holding the lock when this method is called,
		// the calculation may become off by a few items, but it doesn't matter much
		// since the freed size is only an approximation.
		lock (_lock) {
			_freedSize = 0;
		}
	}

	private LinkedListNode<LRUItem> GetNode() {
		if (_nodesPool.Count > 0)
			return _nodesPool.Dequeue();
		return new LinkedListNode<LRUItem>(new LRUItem());
	}

	private void ReturnNode(LinkedListNode<LRUItem> node) {
		// set to default to allow memory to be gced
		node.Value.Key = default;
		node.Value.Value = default;
		_nodesPool.Enqueue(node);
	}
}
