// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using EventStore.Core.DataStructures;
using KurrentDB.Common.Utils;

namespace EventStore.Core.Services.TimerService;

public class TimerBasedScheduler : IScheduler {
	private readonly PairingHeap<ScheduledTask> _tasks = new((x, y) => x.DueTime < y.DueTime);
	private readonly ITimeProvider _timeProvider;
	private readonly ITimer _timer;
	private readonly object _queueLock = new();

	public TimerBasedScheduler(ITimer timer, ITimeProvider timeProvider) {
		_timeProvider = Ensure.NotNull(timeProvider);
		_timer = Ensure.NotNull(timer);
	}

	public void Stop() {
		Dispose();
	}

	public void Schedule(TimeSpan after, Action<IScheduler, object> callback, object state) {
		lock (_queueLock) {
			_tasks.Add(new ScheduledTask(_timeProvider.UtcNow.Add(after), callback, state));
			ResetTimer();
		}
	}

	protected void ProcessOperations() {
		while (_tasks.Count > 0 && _tasks.FindMin().DueTime <= _timeProvider.UtcNow) {
			var scheduledTask = _tasks.DeleteMin();
			scheduledTask.Action(this, scheduledTask.State);
		}
	}

	private void OnTimerFired() {
		lock (_queueLock) {
			ProcessOperations();
			ResetTimer();
		}
	}

	private void ResetTimer() {
		if (_tasks.Count > 0) {
			var tuple = _tasks.FindMin();
			_timer.FireIn((int)(tuple.DueTime - _timeProvider.UtcNow).TotalMilliseconds, OnTimerFired);
		} else {
			_timer.FireIn(Timeout.Infinite, OnTimerFired);
		}
	}

	public void Dispose() {
		_timer.Dispose();
	}

	private struct ScheduledTask(DateTime dueTime, Action<IScheduler, object> action, object state) {
		public readonly DateTime DueTime = dueTime;
		public readonly Action<IScheduler, object> Action = action;
		public readonly object State = state;
	}
}
