// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;

namespace EventStore.Auth.Ldaps;

public class LRUCache<TKey, TValue> {
	private class LRUItem {
		public TKey Key;
		public TValue Value;
	}

	private readonly LinkedList<LRUItem> _orderList = new LinkedList<LRUItem>();

	private readonly Dictionary<TKey, LinkedListNode<LRUItem>> _items =
		new Dictionary<TKey, LinkedListNode<LRUItem>>();

	private readonly Queue<LinkedListNode<LRUItem>> _nodesPool = new Queue<LinkedListNode<LRUItem>>();

	private readonly int _maxCount;
	private readonly object _lock = new object();

	private Func<object, bool>
		_onPut, _onRemove; //_onPut is not called if a key-value pair already exists in the cache

	public LRUCache(int maxCount) {
		if (maxCount < 0) throw new ArgumentException("Should be non negative", nameof(maxCount));
		_maxCount = maxCount;
	}

	public LRUCache(int maxCount, Func<object, bool> onPut, Func<object, bool> onRemove) {
		if (maxCount < 0) throw new ArgumentException("Should be non negative", nameof(maxCount));
		_maxCount = maxCount;
		_onPut = onPut;
		_onRemove = onRemove;
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

			value = default(TValue);
			return false;
		}
	}

	public TValue Put(TKey key, TValue value) {
		lock (_lock) {
			LinkedListNode<LRUItem> node;
			if (!_items.TryGetValue(key, out node)) {
				node = GetNode();
				node.Value.Key = key;
				node.Value.Value = value;

				EnsureCapacity();

				_items.Add(key, node);
				_orderList.AddLast(node);

				if (_onPut != null) _onPut(node.Value.Value);
			} else {
				node.Value.Value = value;

				if (!ReferenceEquals(node, _orderList.Last)) {
					_orderList.Remove(node);
					_orderList.AddLast(node);
				}
			}

			return value;
		}
	}

	public void Remove(TKey key) {
		lock (_lock) {
			LinkedListNode<LRUItem> node;
			if (_items.TryGetValue(key, out node)) {
				_orderList.Remove(node);
				_items.Remove(key);
				if (_onRemove != null) _onRemove(node.Value.Value);

				ReturnNode(node);
			}
		}
	}

	public void Clear() {
		lock (_lock) {
			while (_orderList.Count > 0) {
				var node = _orderList.First;
				_orderList.RemoveFirst();
				_items.Remove(node.Value.Key);
				if (_onRemove != null) _onRemove(node.Value.Value);

				ReturnNode(node);
			}
		}
	}

	public TValue Put<T>(TKey key, T userData, Func<TKey, T, TValue> addFactory,
		Func<TKey, TValue, T, TValue> updateFactory) {
		lock (_lock) {
			LinkedListNode<LRUItem> node;
			if (!_items.TryGetValue(key, out node)) {
				node = GetNode();
				node.Value.Key = key;
				node.Value.Value = addFactory(key, userData);

				EnsureCapacity();

				_items.Add(key, node);
				_orderList.AddLast(node);
				if (_onPut != null) _onPut(node.Value.Value);
			} else {
				node.Value.Value = updateFactory(key, node.Value.Value, userData);

				if (!ReferenceEquals(node, _orderList.Last)) {
					_orderList.Remove(node);
					_orderList.AddLast(node);
				}
			}

			return node.Value.Value;
		}
	}

	private void EnsureCapacity() {
		while (_items.Count > 0 && _items.Count >= _maxCount) {
			var node = _orderList.First;
			_orderList.Remove(node);
			_items.Remove(node.Value.Key);
			if (_onRemove != null) _onRemove(node.Value.Value);

			ReturnNode(node);
		}
	}

	private LinkedListNode<LRUItem> GetNode() {
		if (_nodesPool.Count > 0)
			return _nodesPool.Dequeue();
		return new LinkedListNode<LRUItem>(new LRUItem());
	}

	private void ReturnNode(LinkedListNode<LRUItem> node) {
		_nodesPool.Enqueue(node);
	}
}
