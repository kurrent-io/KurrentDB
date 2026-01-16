// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.Monitoring.Stats;
using KurrentDB.Core.Time;

namespace KurrentDB.Core.Metrics;

// Monitors the length in seconds of the thread pool queue for the metrics
// Monitors the length in items of the thread pool queue for the stats
class ThreadPoolQueueLengthMonitor : IMonitoredQueue, IThreadPoolWorkItem, IDisposable {
	readonly QueueStatsCollector _queueStats;
	readonly Timer _timer;
	readonly TimeSpan _delay;
	readonly QueueTracker _tracker;
	Instant _enqueuedAt;

	public ThreadPoolQueueLengthMonitor(
		TimeSpan delay,
		QueueTrackers trackers,
		QueueStatsManager queueStatsManager) {

		_timer = new Timer(_ => Enqueue());
		_delay = delay;
		_tracker = trackers.GetTrackerForQueue("ThreadPoolQueue");
		_queueStats = queueStatsManager.CreateQueueStatsCollector("ThreadPool");
	}

	public string Name => _queueStats.Name;

	public void Dispose() {
		_timer.Dispose();
	}

	public void Start() {
		Enqueue();
		QueueMonitor.Default.Register(this);
	}

	public void Stop() {
		QueueMonitor.Default.Unregister(this);
	}

	void Enqueue() {
		_enqueuedAt = _tracker.Now;
		ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
	}

	void IThreadPoolWorkItem.Execute() {
		_queueStats.ReportQueueLength(CurrentQueueLength);
		_tracker.RecordMessageDequeued(_enqueuedAt);
		_timer.Change(dueTime: _delay, period: Timeout.InfiniteTimeSpan);
	}

	QueueStats IMonitoredQueue.GetStatistics() =>
		_queueStats.GetStatistics(CurrentQueueLength);

	static int CurrentQueueLength => int.CreateSaturating(ThreadPool.PendingWorkItemCount);
}
