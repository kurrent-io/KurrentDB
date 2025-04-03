// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Monitoring.Stats;

namespace EventStore.Core.Bus;

public class QueueStatsCollector {
	private static readonly TimeSpan MinRefreshPeriod = TimeSpan.FromMilliseconds(100);

	public readonly string Name;
	public readonly string GroupName;

	public Type InProgressMessage {
		get { return _inProgressMsgType; }
	}

	private readonly object
		_statisticsLock =
			new object(); // this lock is mostly acquired from a single thread (+ rarely to get statistics), so performance penalty is not too high

	private readonly Stopwatch _busyWatch = new Stopwatch();
	private readonly Stopwatch _idleWatch = new Stopwatch();
	private readonly Stopwatch _totalIdleWatch = new Stopwatch();
	private readonly Stopwatch _totalBusyWatch = new Stopwatch();
	private readonly Stopwatch _totalTimeWatch = new Stopwatch();
	private TimeSpan _lastTotalIdleTime;
	private TimeSpan _lastTotalBusyTime;
	private TimeSpan _lastTotalTime;

	private long _totalItems;
	private long _lastTotalItems;
	private int _lifetimeQueueLengthPeak;
	private int _currentQueueLengthPeak;
	private Type _lastProcessedMsgType;
	private Type _inProgressMsgType;

	private bool _wasIdle;

#if DEBUG
	private volatile bool _started;
	private volatile bool _stopped;
	private int _length;
#endif
	public QueueStatsCollector(string name, string groupName = null) {
		Ensure.NotNull(name, "name");

		Name = name;
		GroupName = groupName;
	}

	public void Start() {
		_totalTimeWatch.Start();
#if DEBUG
		Debug.Assert(!_started,
			string.Format("QueueStatsCollector [{0}] was already started when Start() entered", Name));
		Debug.Assert(!_stopped,
			string.Format("QueueStatsCollector [{0}] was stopped when Start() entered", Name));
		_started = true;
#endif
		EnterIdle();
	}

	public void Stop() {
#if DEBUG
		Debug.Assert(_started,
			string.Format("QueueStatsCollector [{0}] was not started when Stop() entered", Name));
		Debug.Assert(!_stopped,
			string.Format("QueueStatsCollector [{0}] was already stopped when Stop() entered", Name));
#endif
		EnterIdle();
		_totalTimeWatch.Stop();
#if DEBUG
		_stopped = true;
#endif
	}

	public void ProcessingStarted<T>(int queueLength) {
		ProcessingStarted(typeof(T), queueLength);
	}

	public void ProcessingStarted(Type msgType, int queueLength) {
		_lifetimeQueueLengthPeak = _lifetimeQueueLengthPeak > queueLength ? _lifetimeQueueLengthPeak : queueLength;
		_currentQueueLengthPeak = _currentQueueLengthPeak > queueLength ? _currentQueueLengthPeak : queueLength;

		_inProgressMsgType = msgType;
	}

	public void ProcessingEnded(int itemsProcessed) {
		Interlocked.Add(ref _totalItems, itemsProcessed);
		_lastProcessedMsgType = _inProgressMsgType;
		_inProgressMsgType = null;
	}

	public void EnterIdle() {
#if DEBUG
		Debug.Assert(_started,
			string.Format("QueueStatsCollector [{0}] was not started when EnterIdle() entered", Name));
#endif
		if (_wasIdle)
			return;
		_wasIdle = true;

		//NOTE: the following locks are primarily acquired in main thread,
		//      so not too high performance penalty
		lock (_statisticsLock) {
			_totalIdleWatch.Start();
			_idleWatch.Restart();

			_totalBusyWatch.Stop();
			_busyWatch.Reset();
		}
	}

	public void EnterBusy() {
#if DEBUG
		Debug.Assert(_started,
			string.Format("QueueStatsCollector [{0}] was not started when EnterBusy() entered", Name));
#endif
		if (!_wasIdle)
			return;
		_wasIdle = false;

		lock (_statisticsLock) {
			_totalIdleWatch.Stop();
			_idleWatch.Reset();

			_totalBusyWatch.Start();
			_busyWatch.Restart();
		}
	}

	public QueueStats GetStatistics(int currentQueueLength) {
		lock (_statisticsLock) {
			var totalTime = _totalTimeWatch.Elapsed;
			var totalIdleTime = _totalIdleWatch.Elapsed;
			var totalBusyTime = _totalBusyWatch.Elapsed;
			var totalItems = Interlocked.Read(ref _totalItems);

			var lastRunMs = totalTime - _lastTotalTime;
			var lastItems = totalItems - _lastTotalItems;
			var avgItemsPerSecond = lastRunMs.Ticks != 0
				? (int)(TimeSpan.TicksPerSecond * lastItems / lastRunMs.Ticks)
				: 0;
			var avgProcessingTime =
				lastItems != 0 ? (totalBusyTime - _lastTotalBusyTime).TotalMilliseconds / lastItems : 0;
			var idleTimePercent = Math.Min(100.0,
				lastRunMs.Ticks != 0 ? 100.0 * (totalIdleTime - _lastTotalIdleTime).Ticks / lastRunMs.Ticks : 100);

			var stats = new QueueStats(
				Name,
				GroupName,
				currentQueueLength,
				avgItemsPerSecond,
				avgProcessingTime,
				idleTimePercent,
				_busyWatch.IsRunning ? _busyWatch.Elapsed : (TimeSpan?)null,
				_idleWatch.IsRunning ? _idleWatch.Elapsed : (TimeSpan?)null,
				totalItems,
				_currentQueueLengthPeak,
				_lifetimeQueueLengthPeak,
				_lastProcessedMsgType,
				_inProgressMsgType);

			if (totalTime - _lastTotalTime >= MinRefreshPeriod) {
				_lastTotalTime = totalTime;
				_lastTotalIdleTime = totalIdleTime;
				_lastTotalBusyTime = totalBusyTime;
				_lastTotalItems = totalItems;

				_currentQueueLengthPeak = 0;
			}

			return stats;
		}
	}

	[Conditional("DEBUG")]
	public void Enqueued() {
#if DEBUG
		Interlocked.Increment(ref _length);
#endif
	}

	[Conditional("DEBUG")]
	public void Dequeued(Message msg) {
#if DEBUG
		Debug.Assert(_started,
			string.Format("QueueStatsCollector [{0}] was not started when Dequeued() entered", Name));
		Debug.Assert(!_stopped,
			string.Format("QueueStatsCollector [{0}] was stopped when Dequeued() entered", Name));
		Interlocked.Decrement(ref _length);
#endif
	}

#if DEBUG
	public bool IsIdle() {
		var curLength = Interlocked.CompareExchange(ref _length, 0, 0);
		return !_started || _stopped || curLength == 0;
	}

	public bool IsStopped() {
		return _stopped;
	}
#endif
}
