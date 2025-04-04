// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;

namespace EventStore.Core.DataStructures;

public class BoundedQueue<T> {
	private readonly int _maxCapacity;
	private readonly Queue<T> _queue;

	public int MaxCapacity {
		get { return _maxCapacity; }
	}

	public int Count {
		get { return _queue.Count; }
	}

	public BoundedQueue(int maxCapacity) {
		_queue = new Queue<T>(maxCapacity);
		_maxCapacity = maxCapacity;
	}

	public void Enqueue(T obj) {
		if (_queue.Count >= _maxCapacity)
			Dequeue();
		_queue.Enqueue(obj);
	}

	public T Dequeue() {
		return _queue.Dequeue();
	}

	public T Peek() {
		return _queue.Peek();
	}

	public bool CanAccept() {
		return _queue.Count < _maxCapacity;
	}
}
