// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;

namespace KurrentDB.Core.Bus;

partial class ThreadPoolMessageScheduler {
	private volatile AsyncStateMachine _firstNode;

	private void ReturnToPool(AsyncStateMachine node) {
		AsyncStateMachine current;
		do {
			current = _firstNode;
			node.NextInPool = current;
		} while (Interlocked.CompareExchange(ref _firstNode, node, current) != current);
	}

	private AsyncStateMachine RentFromPool() {
		AsyncStateMachine current;
		do {
			current = _firstNode;

			if (current is null) {
				current = new PoolingAsyncStateMachine(this);
				break;
			}
		} while (Interlocked.CompareExchange(ref _firstNode, current.NextInPool, current) != current);

		current.NextInPool = null;
		return current;
	}

	private partial class AsyncStateMachine {
		internal AsyncStateMachine NextInPool;

		protected void ReturnToPool() => _scheduler.ReturnToPool(this);
	}
}
