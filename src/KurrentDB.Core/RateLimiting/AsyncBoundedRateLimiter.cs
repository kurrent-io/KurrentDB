// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext;

namespace KurrentDB.Core.RateLimiting;

public sealed partial class AsyncBoundedRateLimiter : Disposable {
	private readonly object _syncRoot;
	private int _leasesAvailable;

	public AsyncBoundedRateLimiter(int concurrencyLimit, int maxQueueSize) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrencyLimit);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxQueueSize);

		_leasesAvailable = concurrencyLimit;
		_maxQueueSize = maxQueueSize;

		// we need to cover cached wait nodes for incoming queue (which is bounded)
		// and in-flight queue (which is unbounded). But we assume that the size
		// of the in-flight queue should not be too high, so keep maxQueueSize / 2 of nodes
		// for the in-flight queue.
		_maxPoolSize = int.CreateSaturating(maxQueueSize / 2 * 3L);
		_syncRoot = new();
	}

	/// <summary>
	/// Acquires the lease.
	/// </summary>
	/// <param name="prioritized"></param>
	/// <param name="token"></param>
	/// <returns>
	/// <see langword="true"/> if the lease is acquired successfully;
	/// <see langword="false"/> if the rate limit is reached and <paramref name="prioritized"/>
	/// is <see langword="false"/>.
	/// </returns>
	public ValueTask<bool> AcquireAsync(bool prioritized, CancellationToken token = default) {
		ISupplier<TimeSpan, CancellationToken, ValueTask<bool>> factory;
		lock (_syncRoot) {
			if (IsDisposingOrDisposed) {
				// produce the task with ObjectDisposedException
				factory = DisposedTaskFactory.Instance;
			} else if (_leasesAvailable > 0) {
				// leases are available, complete synchronously
				_leasesAvailable--;
				factory = CompletedTaskFactory.Instance;
			} else if (token.IsCancellationRequested) {
				// canceled, complete synchronously
				factory = CanceledTaskFactory.Instance;
			} else {
				// slow path - create a node that implements IValueTaskSource
				factory = EnqueueNode(prioritized);
			}
		}

		// activate the completion source out of the lock to reduce the lock contention
		return factory.Invoke(Timeout.InfiniteTimeSpan, token);
	}

	/// <summary>
	/// Releases the lease previously acquired with <see cref="AcquireAsync"/>.
	/// </summary>
	public void Release() {
		WaitNode suspendedCaller;
		lock (_syncRoot) {
			// do not throw if Dispose() is in progress
			if (!_inFlightQueue.SignalFirst(out suspendedCaller) && !_incomingQueue.SignalFirst(out suspendedCaller)) {
				// Both queues are empty, just increase the number of available leases
				_leasesAvailable++;
			}
		}

		suspendedCaller?.NotifyConsumer();
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			NotifyObjectDisposed();
		}

		base.Dispose(disposing);
	}

	private void NotifyObjectDisposed() {
		var e = CreateException();
		lock (_syncRoot) {
			_incomingQueue.NotifyObjectDisposed(e);
			_incomingQueue.Clear();

			_inFlightQueue.NotifyObjectDisposed(e);
			_inFlightQueue.Clear();
		}
	}
}
