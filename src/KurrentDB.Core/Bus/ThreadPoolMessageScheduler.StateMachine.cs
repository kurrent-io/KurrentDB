// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using DotNext.Threading;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Bus;

partial class ThreadPoolMessageScheduler {
	// WARNING: any mutations of AsyncStateMachine must be done before this method, otherwise,
	// race condition could happen
	private void ProcessingCompleted() {
		if (Interlocked.Decrement(ref _processingCount) is 0U && _lifetimeToken.IsCancellationRequested)
			_stopNotification.TrySetResult();
	}

	// Custom async state machine allows to avoid mem allocations since the instance can be
	// reused multiple times. Execution procedure is effectively 'async void' method that
	// doesn't have any awaiters (in contrast to ValueTask or Task). Thus, it's not possible
	// to apply PoolingAsyncValueTaskMethodBuilder for that method.
	// We register different callbacks rather than storing an explicit state variable.
	// The meaning of _awaiter depends on the state that we are in.
	private class AsyncStateMachine : IThreadPoolWorkItem {
		private readonly ThreadPoolMessageScheduler _scheduler;
		private readonly Action _onConsumerCompleted;
		private readonly Action _onLockAcquisitionCompleted;

		// state fields
		private ConfiguredValueTaskAwaitable.ConfiguredValueTaskAwaiter _awaiter;
		private Message _message;
		private AsyncExclusiveLock _groupLock;

		public AsyncStateMachine(ThreadPoolMessageScheduler scheduler) {
			_scheduler = scheduler;
			_onConsumerCompleted = OnConsumerCompleted;
			_onLockAcquisitionCompleted = OnLockAcquisitionCompleted;
		}

		protected virtual void ProcessingCompleted() {
			CleanUp();
			_scheduler.ProcessingCompleted();
		}

		// true if acquired successfully
		// false if canceled
		private bool CheckLockAcquisition() {
			try {
				// We must consume the result, even if it's void. This is required by ValueTask behavioral contract.
				_awaiter.GetResult();
			} catch (Exception e) {
				ProcessingCompleted();
				if (e is OperationCanceledException canceledEx &&
				    canceledEx.CancellationToken == _scheduler._lifetimeToken) {
					return false;
				}

				// not possible to get here. throwing here on the thread pool will exit the application
				throw;
			}

			_awaiter = default;
			return true;
		}

		private void OnLockAcquisitionCompleted() {
			if (CheckLockAcquisition()) {
				// We are already on the thread pool so we can invoke directly.
				InvokeConsumer();
			}
		}

		[SuppressMessage("Reliability", "CA2012", Justification = "The state machine is coded manually")]
		private void AcquireAndQueueOnThreadPool() {
			Debug.Assert(_message is not null);
			Debug.Assert(_groupLock is not null);

			// start the lock acquisition
			_awaiter = _groupLock
				.AcquireAsync(_scheduler._lifetimeToken)
				.ConfigureAwait(false)
				.GetAwaiter();

			if (!_awaiter.IsCompleted) {
				// the lock cannot be acquired synchronously, attach the callback
				// to be called when the lock is acquired
				_awaiter.UnsafeOnCompleted(_onLockAcquisitionCompleted);
			} else if (CheckLockAcquisition()) {
				// acquired synchronously without exceptions
				QueueOnThreadPool();
			}
		}


		// The current state machine implements approximately the following implementation:
#if DEBUG
		public async void ScheduleAsync(Message message, AsyncExclusiveLock groupLock){
			try {
				if (groupLock is not null)
					await groupLock.AcquireAsync(_scheduler._lifetimeToken);

				await _scheduler._consumer(message, _scheduler._lifetimeToken);
			} catch(OperationCanceledException e) when (e.CancellationToken == _scheduler._lifetimeToken) {
				// do nothing
			} finally {
				groupLock?.Release();
				ProcessingCompleted();
			}
		}
#endif

		internal void Schedule(Message message, AsyncExclusiveLock groupLock) {
			_message = message;
			_groupLock = groupLock;

			if (groupLock is null) {
				// no synchronization group provided, simply enqueue the processing to the thread pool
				QueueOnThreadPool();
			} else {
				// acquire the lock first to preserve the correct ordering
				AcquireAndQueueOnThreadPool();
			}
		}

		private void CleanUp() {
			_message = null;
			_groupLock = null;
			_awaiter = default;
		}

		private void OnConsumerCompleted() {
			try {
				_awaiter.GetResult();
			} catch (OperationCanceledException e) when (e.CancellationToken == _scheduler._lifetimeToken) {
				// suspend
			} finally {
				_groupLock?.Release();
				ProcessingCompleted();
			}
		}

		[SuppressMessage("Reliability", "CA2012", Justification = "The state machine is coded manually")]
		private void InvokeConsumer() {
			// ALWAYS called on the thread pool.
			_awaiter = _scheduler
				._consumer(_message, _scheduler._lifetimeToken)
				.ConfigureAwait(false)
				.GetAwaiter();

			if (_awaiter.IsCompleted) {
				OnConsumerCompleted();
			} else {
				_awaiter.UnsafeOnCompleted(_onConsumerCompleted);
			}
		}

		private void QueueOnThreadPool() => ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);

		void IThreadPoolWorkItem.Execute() => InvokeConsumer();
	}

	private sealed class PoolingAsyncStateMachine(ThreadPoolMessageScheduler scheduler) : AsyncStateMachine(scheduler) {
		readonly ThreadPoolMessageScheduler _scheduler = scheduler;

		protected override void ProcessingCompleted() {
			base.ProcessingCompleted();
			_scheduler._pool.Add(this);
		}
	}
}
