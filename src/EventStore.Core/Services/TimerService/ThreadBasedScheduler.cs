using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.DataStructures;
using EventStore.Core.Metrics;
using EventStore.Core.Services.Monitoring.Stats;
using EventStore.Core.Time;
using Serilog;

namespace EventStore.Core.Services.TimerService {
	public class ThreadBasedScheduler : IMonitoredQueue, IScheduler, IDisposable {
		private static readonly ILogger Log = Serilog.Log.ForContext<ThreadBasedScheduler>();
		public string Name {
			get { return _queueStats.Name; }
		}

		private readonly ConcurrentQueueWrapper<ScheduledTask> _pending = new ConcurrentQueueWrapper<ScheduledTask>();

		private readonly PairingHeap<ScheduledTask> _tasks =
			new PairingHeap<ScheduledTask>((x, y) => x.DueTime < y.DueTime);

		private readonly IClock _timeProvider;

		private readonly Thread _timerThread;
		private volatile bool _stop;

		private readonly QueueStatsCollector _queueStats;
		private readonly QueueTracker _tracker;
		private readonly TaskCompletionSource<object> _tcs = new TaskCompletionSource<object>();

		public Task Task {
			get { return _tcs.Task; }
		}

		public ThreadBasedScheduler(
			QueueStatsManager queueStatsManager,
			QueueTrackers trackers,
			IClock timeProvider = null) {

			_timeProvider = timeProvider ?? Clock.Instance;
			_queueStats = queueStatsManager.CreateQueueStatsCollector("Timer");
			_tracker = trackers.GetTrackerForQueue("Timer");

			_timerThread = new Thread(DoTiming);
			_timerThread.IsBackground = true;
			_timerThread.Name = Name;
			_timerThread.Start();
		}

		public void Stop() {
			Dispose();
		}

		public void Schedule(TimeSpan after, Action<IScheduler, object> callback, object state) {
			_pending.Enqueue(new ScheduledTask(_timeProvider.Now.Add(after), callback, state));
		}

		private void DoTiming() {

			_queueStats.Start();
			QueueMonitor.Default.Register(this);

			while (!_stop) {
				try {
					_queueStats.EnterBusy();
					_queueStats.ProcessingStarted<SchedulePendingTasks>(_pending.Count);

					int pending = 0;
					ScheduledTask task;
					while (_pending.TryDequeue(out task)) {
						_tasks.Add(task);
						pending += 1;
					}

					_queueStats.ProcessingEnded(pending);

					_queueStats.ProcessingStarted<ExecuteScheduledTasks>(_tasks.Count);
					int processed = 0;
					while (_tasks.Count > 0 && _tasks.FindMin().DueTime <= _timeProvider.Now) {
						processed += 1;
						var scheduledTask = _tasks.DeleteMin();
						var now = _tracker.RecordMessageDequeued(enqueuedAt: scheduledTask.DueTime);
						scheduledTask.Action(this, scheduledTask.State);
						var label = (scheduledTask.State as TimerMessage.Schedule)?.ReplyMessage.Label;
						_tracker.RecordMessageProcessed(now, label ?? "");
					}

					_queueStats.ProcessingEnded(processed);

					if (processed == 0) {
						_queueStats.EnterIdle();

						Thread.Sleep(10);
					}

				} catch (Exception ex) {
					Log.Error(ex, "Error executing scheduled task");
					_tcs.TrySetException(ex);
				}
			}

			_queueStats.Stop();
			QueueMonitor.Default.Unregister(this);
		}

		public void Dispose() {
			_stop = true;
		}

		public QueueStats GetStatistics() {
			return _queueStats.GetStatistics(_tasks.Count);
		}

		private struct ScheduledTask {
			public readonly Instant DueTime;
			public readonly Action<IScheduler, object> Action;
			public readonly object State;

			public ScheduledTask(Instant dueTime, Action<IScheduler, object> action, object state) {
				DueTime = dueTime;
				Action = action;
				State = state;
			}
		}

		private class SchedulePendingTasks {
		}

		private class ExecuteScheduledTasks {
		}
	}
}
