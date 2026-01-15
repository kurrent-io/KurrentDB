// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Threading;

namespace KurrentDB.Core.Bus;

partial class ThreadPoolMessageScheduler {
	private readonly ConcurrentQueue<AsyncStateMachine> _items = new();
	private AsyncStateMachine _fastItem;

	private void ReturnToPool(AsyncStateMachine item) {
		if (_fastItem is not null || Interlocked.CompareExchange(ref _fastItem, item, null) is not null) {
			_items.Enqueue(item);
		}
	}

	// no ABA problem. if other threads use and return the same _fastItem it is safe because there are no side effects
	private AsyncStateMachine RentFromPool() {
		var result = _fastItem;
		if (result is null || Interlocked.CompareExchange(ref _fastItem, null, result) != result) {
			if (!_items.TryDequeue(out result))
				result = new PoolingAsyncStateMachine(this);
		}

		return result;
	}

	private partial class AsyncStateMachine {
		protected void ReturnToPool() => _scheduler.ReturnToPool(this);
	}
}
