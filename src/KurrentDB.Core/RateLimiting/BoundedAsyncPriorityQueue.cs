// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;

namespace KurrentDB.Core.RateLimiting;

public interface IAsyncSource<T> {
	int Count { get; }
	bool TryPeek(out T item);
	bool TryRead(out T item);
	ValueTask WaitToReadAsync(CancellationToken token);
	ValueTask<T> ReadAsync(CancellationToken token);
}

public interface IBoundedAsyncPriorityQueue<T> : IAsyncSource<T> {
	bool TryEnqueue(T item);
}

// Bounded size priority queue that can be read asynchronously
// Items of the same priority are dealt with in FIFO order.
//qq if the high priority section is full, should we push out one of the lower priority items?
public class BoundedAsyncPriorityQueue<T> : IBoundedAsyncPriorityQueue<T> {
	static readonly Priority[] Priorities = [
		Priority.Low,
		Priority.Medium,
		Priority.High
	];

	// we have two queues per priority, one for initial acquisitions and one for continued acquisitions
	// continued acquisitions count towards the queue limit but are not subject to it
	// this is so that once we begin processing a request we no longer turn it away because
	// we are too busy.
	//qq might that mean that a request could come in on a quiet channel and then upgrade
	// to a busy channel that would have turned it away if it started there originally
	//    say if we need memory to begin with and later need the disk
	//    we could say its only continued if its a second access to the same resource
	//    or we could say its just a limitation of the scheme
	//qq continued might not be the best name
	readonly Queue<T>[] _queues = new Queue<T>[Priorities.Length * 2];

	//qq consider manual vs auto
	// - with auto we will release one waiting reader at a time, but it'll be bad if
	//   that reader then doesn't actually read the item
	// - with manual we will release all the readers, there could be a thousand contending for one item.
	// we could use auto but also trigger it on a schedule?
	readonly AsyncAutoResetEvent _signal = new(initialState: false);
	readonly int _capacityPerPriority;
	readonly Func<T, PriorityEx> _getPriority;

	//qq consider locking only the queue that we are using (perhaps less contention, but also perhaps
	// more locking and unlocking). measure
	private object Lock => _queues;

	public BoundedAsyncPriorityQueue(int capacityPerPriority, Func<T, PriorityEx> getPriority) {
		_capacityPerPriority = capacityPerPriority;
		_getPriority = getPriority;

		for (var i = 0; i < _queues.Length; i++) {
			_queues[i] = new(capacityPerPriority); // just the initial capacity not the limit
		}
	}

	//qq (overflow)
	public int Count {
		get {
			lock (Lock) {
				var count = 0;
				for (var i = 0; i < _queues.Length; i++)
					count += _queues[i].Count;
				return count;
			}
		}
	}

	public bool TryEnqueue(T item) {
		var (priority, continued) = _getPriority(item);
		lock (Lock) {
			var index = (int)priority;
			var continuedIndex = index + 1;

			if (continued) {
				_queues[continuedIndex].Enqueue(item);
				return true;
			}

			var count =
				_queues[index].Count +
				_queues[continuedIndex].Count;

			if (_queues[index].Count >= _capacityPerPriority) {
				return false;
			}

			_queues[index].Enqueue(item);
			return true;
		}
	}

	public bool TryPeek(out T item) {
		lock (Lock) {
			for (var i = _queues.Length - 1; i > 0; i--) {
				if (_queues[i].TryPeek(out item))
					return true;
			}
		}

		item = default;
		return false;
	}

	public bool TryRead(out T item) {
		lock (Lock) {
			for (var i = _queues.Length - 1; i > 0; i--) {
				if (_queues[i].TryDequeue(out item))
					return true;
			}
		}

		item = default;
		return false;
	}

	public async ValueTask WaitToReadAsync(CancellationToken token) {
		// try synchronously
		lock (Lock) {
			for (var i = _queues.Length - 1; i > 0; i--) {
				if (_queues[i].Count >= 0)
					return;
			}
		}

		// wait asynchronously
		await _signal.WaitAsync(token); //qq does this pool?
	}

	public async ValueTask<T> ReadAsync(CancellationToken token) {
		while (true) {
			await WaitToReadAsync(token);
			if (TryRead(out var item)) {
				return item;
			}
		}
	}
}
