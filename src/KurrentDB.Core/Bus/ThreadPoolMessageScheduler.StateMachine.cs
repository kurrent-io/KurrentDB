// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using DotNext;
using DotNext.Threading;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Time;

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
		private Instant _timestamp;

		public AsyncStateMachine(ThreadPoolMessageScheduler scheduler) {
			_scheduler = scheduler;
			_onConsumerCompleted = OnConsumerCompleted;
			_onLockAcquisitionCompleted = OnLockAcquisitionCompleted;
		}

		protected void ReturnToPool() => _scheduler._pool.Add(this);

		// The current state machine implements approximately the following implementation:
		//public async void ScheduleAsync(Message message, AsyncExclusiveLock groupLock){
		//	try {
		//		if (groupLock is not null)
		//			await groupLock.AcquireAsync(_scheduler._lifetimeToken);
		//		await _scheduler._consumer(message, _scheduler._lifetimeToken);
		//	} catch(OperationCanceledException e) when (e.CancellationToken == _scheduler._lifetimeToken) {
		//		// do nothing
		//	} finally {
		//		groupLock?.Release();
		//		ProcessingCompleted();
		//	}
		//}

		internal void Schedule(Message message, AsyncExclusiveLock groupLock) {
			_message = message;
			_groupLock = groupLock;
			ReportEnqueued();

			if (groupLock is null) {
				// no synchronization group provided, simply enqueue the processing to the thread pool
				QueueOnThreadPool();
			} else {
				// acquire the lock first to preserve the correct ordering
				AcquireAndQueueOnThreadPool();
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

		private void OnLockAcquisitionCompleted() {
			if (CheckLockAcquisition()) {
				// We are already on the thread pool so we can invoke directly.
				InvokeConsumer();
			}
		}

		// true if acquired successfully
		// false if canceled
		private bool CheckLockAcquisition() {
			try {
				// We must consume the result, even if it's void. This is required by ValueTask behavioral contract.
				_awaiter.GetResult();
			} catch (Exception e) {
				var knownCancellation = e is OperationCanceledException canceledEx &&
				                        IsKnownCancellation(canceledEx);
				CleanUp();
				ProcessingCompleted();
				if (knownCancellation) {
					return false;
				}

				// not possible to get here. throwing here on the thread pool will exit the application
				throw;
			}

			_awaiter = default;
			return true;
		}

		private void QueueOnThreadPool() => ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);

		void IThreadPoolWorkItem.Execute() => InvokeConsumer();

		[SuppressMessage("Reliability", "CA2012", Justification = "The state machine is coded manually")]
		private void InvokeConsumer() {
			ReportDequeued();

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

		private void OnConsumerCompleted() {
			try {
				_awaiter.GetResult();
			} catch (OperationCanceledException e) when (IsKnownCancellation(e)) {
				// suspend
			} catch (Exception ex) {
				Log.Error(ex,
					"Error while processing message {message} in '{scheduler}'.",
					_message,
					_scheduler.Name);
#if DEBUG
				throw;
#endif
			} finally {
				ReportCompleted();
				_groupLock?.Release();
				CleanUp();
				ProcessingCompleted();
			}
		}

		protected virtual void ProcessingCompleted() => _scheduler.ProcessingCompleted();

		private void CleanUp() {
			_message = null;
			_groupLock = null;
			_awaiter = default;
		}

		private void ReportEnqueued() => _timestamp = NeedsMetrics ? _scheduler._tracker.Now : default;

		private void ReportDequeued() {
			if (NeedsMetrics) {
				_timestamp = _scheduler._tracker.RecordMessageDequeued(_timestamp);

				var queueCnt = _scheduler._processingCount;
				Debug.Assert(queueCnt > 0U);

				queueCnt -= 1U; // exclude the current message
				_scheduler._statsCollector.ProcessingStarted(
					_message.GetType(),
					int.CreateSaturating(queueCnt)); // avoid any overflow exceptions
			}
		}

		// should not be called outside the lock, because the old stats collector is not thread safe
		private void ReportCompleted() {
			if (NeedsMetrics) {
				_scheduler._tracker.RecordMessageProcessed(_timestamp, _message.Label);
				_scheduler._statsCollector.ProcessingEnded(itemsProcessed: 1);
			}
		}

		[MemberNotNullWhen(true, nameof(_groupLock))]
		[MemberNotNullWhen(true, nameof(_message))]
		private bool NeedsMetrics {
			get {
				Debug.Assert(_message is not null);

				return ReferenceEquals(_message.Affinity, Message.UnknownAffinity)
				       && _groupLock is not null;
			}
		}

		private bool IsKnownCancellation(OperationCanceledException e)
			=> e.CancellationToken.IsOneOf([_scheduler._lifetimeToken, _message.CancellationToken]);
	}

	private sealed class PoolingAsyncStateMachine(ThreadPoolMessageScheduler scheduler) : AsyncStateMachine(scheduler) {
		protected override void ProcessingCompleted() {
			base.ProcessingCompleted();
			ReturnToPool();
		}
	}
}
