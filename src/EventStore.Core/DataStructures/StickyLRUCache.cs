// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Common.Utils;

namespace EventStore.Core.DataStructures;

public class StickyLRUCache<TKey, TValue> : IStickyLRUCache<TKey, TValue> {
	private class LRUItem {
		public TKey Key;
		public TValue Value;
		public int Stickiness;
	}

	private readonly LinkedList<LRUItem> _orderList = new LinkedList<LRUItem>();

	private readonly Dictionary<TKey, LinkedListNode<LRUItem>> _items =
		new Dictionary<TKey, LinkedListNode<LRUItem>>();

	private readonly Queue<LinkedListNode<LRUItem>> _nodesPool = new Queue<LinkedListNode<LRUItem>>();

	private readonly int _maxCount;

	public StickyLRUCache(int maxCount) {
		Ensure.Nonnegative(maxCount, "maxCount");

		_maxCount = maxCount;
	}

	public void Clear() {
		while (_orderList.Count > 0) {
			var node = _orderList.First;
			_orderList.RemoveFirst();
			ReturnNode(node);
		}

		_items.Clear();
	}

	public bool TryGet(TKey key, out TValue value) {
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

	public TValue Put(TKey key, TValue value, int stickiness) {
		LinkedListNode<LRUItem> node;
		if (!_items.TryGetValue(key, out node)) {
			node = GetNode();
			node.Value.Key = key;
			node.Value.Value = value;
			node.Value.Stickiness = stickiness;

			EnsureCapacity();

			_items.Add(key, node);
		} else {
			node.Value.Value = value;
			node.Value.Stickiness += stickiness;
			_orderList.Remove(node);
		}

		_orderList.AddLast(node);
		return value;
	}

	public void Remove(TKey key) {
		LinkedListNode<LRUItem> node;
		if (_items.TryGetValue(key, out node)) {
			_orderList.Remove(node);
			_items.Remove(key);
		}
	}

	public TValue Put(TKey key, Func<TKey, TValue> addFactory, Func<TKey, TValue, TValue> updateFactory,
		int stickiness) {
		LinkedListNode<LRUItem> node;
		if (!_items.TryGetValue(key, out node)) {
			node = GetNode();
			node.Value.Key = key;
			node.Value.Value = addFactory(key);
			node.Value.Stickiness = stickiness;

			EnsureCapacity();

			_items.Add(key, node);
		} else {
			node.Value.Value = updateFactory(key, node.Value.Value);
			node.Value.Stickiness += stickiness;
			_orderList.Remove(node);
		}

		_orderList.AddLast(node);
		return node.Value.Value;
	}

	private void EnsureCapacity() {
		while (_items.Count > 0 && _items.Count >= _maxCount) {
			var node = _orderList.First;
			if (node.Value.Stickiness == 0) {
				_orderList.Remove(node);
				_items.Remove(node.Value.Key);
				ReturnNode(node);
			} else {
				_orderList.Remove(node);
				_orderList.AddLast(node);
				break; // hope garbage will be freed on later puts
			}
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
