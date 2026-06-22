// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Indexing;

internal sealed class StreamHashLru(int capacity) {
	readonly LinkedList<ulong> _order = new();
	readonly Dictionary<ulong, LinkedListNode<ulong>> _index = new(capacity);

	public bool TryAdd(ulong hash) {
		if (_index.TryGetValue(hash, out var existing)) {
			_order.Remove(existing);
			_order.AddLast(existing);
			return false;
		}
		if (_index.Count >= capacity) {
			var oldest = _order.First!;
			_index.Remove(oldest.Value);
			_order.RemoveFirst();

			oldest.Value = hash;
			_order.AddLast(oldest);
			_index[hash] = oldest;
		} else {
			_index[hash] = _order.AddLast(hash);
		}
		return true;
	}
}