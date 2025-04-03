// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.DataStructures;
using EventStore.Core.Time;
using EventStore.Core.Services.Monitoring.Stats;
using System.Threading.Tasks;
using EventStore.Core.Metrics;
using Serilog;

namespace EventStore.Core.Services.TimerService;

public sealed class ThreadBasedScheduler : IMonitoredQueue, IScheduler {
	private static readonly ILogger Log = Serilog.Log.ForContext<ThreadBasedScheduler>();
	public string Name => _queueStats.Name;

	private readonly ConcurrentQueueWrapper<ScheduledTask> _pending = new();
	private readonly ManualResetEventSlim _pendingEvent = new(false);
	private readonly PairingHeap<ScheduledTask> _tasks = new((x, y) => x.DueTime < y.DueTime);

	private readonly IClock _timeProvider;

	private volatile bool _stop;

	private readonly QueueStatsCollector _queueStats;
	private readonly QueueTracker _tracker;
	private readonly TaskCompletionSource<object> _tcs = new();

	private long _nextWakeupTimeTicks = long.MinValue;

	public Task Task => _tcs.Task;

	public ThreadBasedScheduler(
		QueueStatsManager queueStatsManager,
		QueueTrackers trackers,
		IClock timeProvider = null) {
		_timeProvider = timeProvider ?? Clock.Instance;
		_queueStats = queueStatsManager.CreateQueueStatsCollector("Timer");
		_tracker = trackers.GetTrackerForQueue("Timer");

		new Thread(DoTiming) {
			IsBackground = true,
			Name = Name
		}.Start();
	}

	public void Stop() {
		Dispose();
	}

	public void Schedule(TimeSpan after, Action<IScheduler, object> callback, object state) {
		var nextWakeup = Interlocked.Read(ref _nextWakeupTimeTicks);
		var now = _timeProvider.Now;

		var dueTime = now.Add(after);
		_pending.Enqueue(new ScheduledTask(dueTime, callback, state));

		// don't unnecessarily wake up the timer thread if it's going to wake up before this task's due time anyway
		if (nextWakeup < now.Ticks || nextWakeup > dueTime.Ticks)
			_pendingEvent.Set();
	}

	private void DoTiming() {
		_queueStats.Start();
		QueueMonitor.Default.Register(this);

		var minTimeout = TimeSpan.FromMilliseconds(1);
		var maxTimeout = TimeSpan.FromSeconds(5);

		while (!_stop) {
			try {
				_queueStats.EnterBusy();
				_queueStats.ProcessingStarted<SchedulePendingTasks>(_pending.Count);

				_pendingEvent.Reset();

				int pending = 0;
				while (_pending.TryDequeue(out var task)) {
					_tasks.Add(task);
					pending += 1;
				}

				_queueStats.ProcessingEnded(pending);

				_queueStats.ProcessingStarted<ExecuteScheduledTasks>(_tasks.Count);
				int processed = 0;

				Instant? nextTaskDueTime;
				while (true) {
					if (_tasks.Count == 0) {
						nextTaskDueTime = null;
						break;
					}

					nextTaskDueTime = _tasks.FindMin().DueTime;
					if (nextTaskDueTime > _timeProvider.Now)
						break;

					processed += 1;
					var scheduledTask = _tasks.DeleteMin();
					var now = _tracker.RecordMessageDequeued(enqueuedAt: scheduledTask.DueTime);
					scheduledTask.Action(this, scheduledTask.State);
					var label = (scheduledTask.State as TimerMessage.Schedule)?.ReplyMessage.Label;
					_tracker.RecordMessageProcessed(now, label ?? "");
				}

				_queueStats.ProcessingEnded(processed);

				if (processed == 0 && !_pendingEvent.IsSet) {
					_queueStats.EnterIdle();

					// give some processor time to other threads since we're free right now
					Thread.Yield();

					var timeout = nextTaskDueTime?.ElapsedTimeSince(_timeProvider.Now) ?? maxTimeout;

					if (timeout <= TimeSpan.Zero)
						// we have already reached the due time of the next task, so we process it immediately
						continue;

					timeout = new TimeSpan(Math.Clamp(timeout.Ticks, minTimeout.Ticks, maxTimeout.Ticks));
					Interlocked.Exchange(ref _nextWakeupTimeTicks, _timeProvider.Now.Add(timeout).Ticks);
					_pendingEvent.Wait(timeout);
				}

			} catch (Exception ex) {
				Log.Error(ex, "Error executing scheduled task");
				_tcs.TrySetException(ex);
			}
		}

		_queueStats.Stop();
		QueueMonitor.Default.Unregister(this);
		_pendingEvent.Dispose();
	}

	public void Dispose() {
		_stop = true;
	}

	public QueueStats GetStatistics() {
		return _queueStats.GetStatistics(_tasks.Count);
	}

	private struct ScheduledTask(Instant dueTime, Action<IScheduler, object> action, object state) {
		public readonly Instant DueTime = dueTime;
		public readonly Action<IScheduler, object> Action = action;
		public readonly object State = state;
	}

	private class SchedulePendingTasks;

	private class ExecuteScheduledTasks;
}
