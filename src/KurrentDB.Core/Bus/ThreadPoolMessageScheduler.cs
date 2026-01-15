// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Metrics;
using Serilog;

namespace KurrentDB.Core.Bus;

/// <summary>
/// Handles messages by scheduling them for consumption on the thread pool by the consumer.
/// Unlike QueuedHandlerThreadPool this is not a queue:
/// Messages can be consumed concurrently by the consumer, depending on their Affinity.
/// </summary>
public partial class ThreadPoolMessageScheduler : IQueuedHandler {
	private static readonly TimeSpan DefaultStopWaitTimeout = TimeSpan.FromSeconds(10);
	private static readonly ILogger Log = Serilog.Log.ForContext<ThreadPoolMessageScheduler>();

	private readonly Func<Message, CancellationToken, ValueTask> _consumer;
	private readonly CancellationToken _lifetimeToken; // cached to avoid ObjectDisposedException
	private readonly TaskCompletionSource _stopNotification;

	private volatile CancellationTokenSource _lifetimeSource;
	private volatile uint _processingCount;
	private volatile TaskCompletionSource _readinessBarrier;

	public ThreadPoolMessageScheduler(string name, IAsyncHandle<Message> consumer) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(consumer);

		_lifetimeToken = (_lifetimeSource = new CancellationTokenSource()).Token;
		_stopNotification = new();

		// Pef: devirt interface
		_consumer = consumer.HandleAsync;
		_readinessBarrier = new();

		// Backward Compatibility Notes:
		// by default, we do not report any metrics.
		// The metrics must be reported only if the scheduler is
		// configured to synchronize the messages with unknown affinity. In that case, the synchronization
		// group associated with the unknown affinity reports the metrics. Any other sync groups do not
		// report any metrics even if tracker/collector is defined.
		_tracker = new(name, IDurationMaxTracker.NoOp, IQueueProcessingTracker.NoOp);
		_statsCollector = IQueueStatsCollector.NoOp;
		Name = name;
	}

	public int MaxPoolSize {
		get;
		init => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
	} = Environment.ProcessorCount * 16;

	public TimeSpan StopTimeout {
		get;
		init => field = value >= TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value));
	} = DefaultStopWaitTimeout;

	/// <summary>
	/// Gets or sets the message processing strategy.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
	public required MessageProcessingStrategy Strategy {
		get;
		init => field = value ?? throw new ArgumentNullException(nameof(value));
	}

	public string Name { get; }

	public void Start() {
		if (Interlocked.Exchange(ref _readinessBarrier, null) is { } completionSource) {
			completionSource.SetResult();
			_queueLengthListener = Strategy.CreateQueueLengthListener(_statsCollector, out _queueLengthObserver);

			if (_queueLengthListener is not null) {
				_queueLengthListener.Start();
				_statsCollector.Start();
				Monitor.Register(this);
			}
		}
	}

	public void RequestStop() {
		if (Interlocked.Exchange(ref _lifetimeSource, null) is { } cts) {
			cts.Cancel();
			if (_queueLengthListener is not null) {
				Monitor.Unregister(this);
				_statsCollector.Stop();
				_queueLengthListener.Dispose();
				_queueLengthObserver = null;
			}
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

	public void Publish(Message message) {
		var messageCount = Interlocked.Increment(ref _processingCount);

		if (_lifetimeToken.IsCancellationRequested) {
			if (Interlocked.Decrement(ref _processingCount) is 0U)
				_stopNotification.TrySetResult();

			return;
		}

		var stateMachine = messageCount > MaxPoolSize
			? new(this)
			: RentFromPool();

		// TODO: We need to respect readiness barrier here and delay messages if the scheduler is not yet started
		stateMachine.Schedule(message, Strategy.GetSynchronizationGroup(message.Affinity));
	}
}
