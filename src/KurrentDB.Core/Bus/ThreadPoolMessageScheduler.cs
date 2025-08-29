// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Bus;

/// <summary>
/// Handles messages by scheduling them for consumption on the thread pool by the consumer.
/// Unlike QueuedHandlerThreadPool this is not a queue:
/// Messages can consumed concurrently by the consumer, depending on their Affinity.
/// </summary>
public partial class ThreadPoolMessageScheduler : IQueuedHandler {
	private static readonly TimeSpan DefaultStopWaitTimeout = TimeSpan.FromSeconds(10);

	private readonly Func<Message, CancellationToken, ValueTask> _consumer;
	private readonly CancellationToken _lifetimeToken; // cached to avoid ObjectDisposedException

	// ConditionalWeakTable does not keep the keys alive, they are removed from the table when
	// they are garbage collected. It is thread safe.
	// We use it to associate AsyncExclusiveLocks with Affinity objects.
	private readonly ConditionalWeakTable<object, AsyncExclusiveLock> _syncGroups;
	private readonly ConcurrentBag<AsyncStateMachine> _pool;
	private readonly TaskCompletionSource _stopNotification;
	private readonly int _maxPoolSize;

	private volatile CancellationTokenSource _lifetimeSource;
	private volatile uint _processingCount;
	private volatile TaskCompletionSource _readinessBarrier;

	public ThreadPoolMessageScheduler(IAsyncHandle<Message> consumer) {
		ArgumentNullException.ThrowIfNull(consumer);

		_lifetimeToken = (_lifetimeSource = new CancellationTokenSource()).Token;
		_syncGroups = new();
		_stopNotification = new();

		// Pef: devirt interface
		_consumer = consumer.HandleAsync;
		_pool = new();
		StopTimeout = DefaultStopWaitTimeout;
		_maxPoolSize = Environment.ProcessorCount * 16;
		_readinessBarrier = new();
	}

	public int MaxPoolSize {
		get => _maxPoolSize;
		init => _maxPoolSize = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
	}

	public TimeSpan StopTimeout {
		get;
		init;
	}

	public required bool SynchronizeMessagesWithUnknownAffinity {
		get;
		init;
	}

	public required string Name { get; init; }

	public void Start() {
		if (Interlocked.Exchange(ref _readinessBarrier, null) is { } completionSource) {
			completionSource.SetResult();
		}
	}

	public void RequestStop() {
		if (Interlocked.Exchange(ref _lifetimeSource, null) is { } cts) {
			cts.Cancel();
		}
	}

	public async Task Stop() {
		RequestStop();

		if (_processingCount is 0U)
			_stopNotification.TrySetResult();

		var timeoutSource = new CancellationTokenSource(StopTimeout);
		try {
			await _stopNotification.Task.WaitAsync(timeoutSource.Token);
		} catch (OperationCanceledException ex) when (ex.CancellationToken == timeoutSource.Token) {
			throw new TimeoutException($"Unable to stop thread '{Name}'.");
		} catch (Exception) {
			// ignore any other exceptions
		} finally {
			timeoutSource.Dispose();
		}
	}

	private TagList CreateMeasurementTags(object affinity) => new() {
		{ "Scheduler", Name },
		{ "SynchronizationGroup", affinity.ToString() }
	};

	// If two messages use the same affinity object, they execute sequentially and in order,
	// Unless the affinity object is Message.UnknownAffinity, which acts as null unless
	// SynchronizeMessagesWithUnknownAffinity is true.
	private AsyncExclusiveLock GetSynchronizationGroup(Message message) {
		var affinity = message.Affinity;
		AsyncExclusiveLock syncGroup;
		if (affinity is null ||
		    (ReferenceEquals(Message.UnknownAffinity, affinity) && !SynchronizeMessagesWithUnknownAffinity)) {
			syncGroup = null;
		} else {
			while (!_syncGroups.TryGetValue(affinity, out syncGroup)) {
				syncGroup = new() { MeasurementTags = CreateMeasurementTags(affinity) };
				if (_syncGroups.TryAdd(affinity, syncGroup))
					break;

				syncGroup.Dispose();
			}
		}

		return syncGroup;
	}

	public void Publish(Message message) {
		var messageCount = Interlocked.Increment(ref _processingCount);

		if (_lifetimeToken.IsCancellationRequested) {
			if (Interlocked.Decrement(ref _processingCount) is 0U)
				_stopNotification.TrySetResult();

			return;
		}

		AsyncStateMachine stateMachine;
		if (messageCount > _maxPoolSize) {
			stateMachine = new(this);
		}
		else if (!_pool.TryTake(out stateMachine)) {
			stateMachine = new PoolingAsyncStateMachine(this);
		}

		var synchronizationGroup = GetSynchronizationGroup(message);

		if (_readinessBarrier is { Task : { IsCompleted: false } readinessTask }) {
			readinessTask.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(Schedule);
		} else {
			Schedule();
		}

		void Schedule() {
			stateMachine.Schedule(message, synchronizationGroup);
		}
	}
}
