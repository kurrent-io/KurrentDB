// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DotNext;

namespace KurrentDB.Core.RateLimiting;

partial class AsyncBoundedRateLimiter {
	private readonly int _maxQueueSize;
	private WaitQueue _incomingQueue, _inFlightQueue;

	private ISupplier<TimeSpan, CancellationToken, ValueTask<bool>> EnqueueNode(bool prioritized) {
		Debug.Assert(Monitor.IsEntered(_syncRoot));

		ref var queue = ref Unsafe.NullRef<WaitQueue>();
		if (prioritized) {
			queue = ref _inFlightQueue;
		} else if (_incomingQueue.Count >= _maxQueueSize) {
			return RejectedTaskFactory.Instance;
		} else {
			queue = ref _incomingQueue;
		}

		var node = RentNode();
		node.Initialize(this, prioritized);
		queue.Add(node);
		return node;
	}

	// should be called only when the source of ValueTask<bool> is consumed
	private void ReturnNode(WaitNode node) {
		if (node.NeedsRemoval) {
			RemoveNode(node);
		}

		// reset the node for further reuse and return it back to the pool
		if (node.TryReset(out _) && !IsDisposingOrDisposed) {
			BackToPool(node);
		}
	}

	private void RemoveNode(WaitNode node) {
		ref var queue = ref node.IsPrioritized
			? ref _inFlightQueue
			: ref _incomingQueue;

		lock (_syncRoot) {
			queue.Remove(node);
		}
	}

	[StructLayout(LayoutKind.Auto)]
	private struct WaitQueue {
		private WaitNodeList _list;
		private int _count;

		public void Add(WaitNode node) {
			_list.Add(node);
			_count++;
		}

		public readonly int Count => _count;

		public void Remove(WaitNode node) {
			_list.Remove(node);
			_count--;

			Debug.Assert(_count >= 0);
		}

		public void Clear() => _list = default;

		public readonly void NotifyObjectDisposed(ObjectDisposedException e) {
			for (WaitNode current = _list.First; current is not null; current = current.Next) {
				current.TrySetException(e);
			}
		}

		public bool SignalFirst(out WaitNode suspendedCaller) {
			for (WaitNode current = _list.First, next; current is not null; current = next) {
				next = current.Next;

				// Possibly, the node is already in signaled state (triggered by cancellation concurrently).
				// In that case, it should not be considered as alive suspended caller.
				// Skip it and try to resume the next node.
				if (!current.TrySignal(out var resumable))
					continue;

				Remove(current);
				suspendedCaller = resumable ? current : null;
				return true;
			}

			suspendedCaller = null;
			return false;
		}
	}
}
