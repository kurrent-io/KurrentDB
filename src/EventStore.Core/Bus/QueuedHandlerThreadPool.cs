using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Metrics;
using EventStore.Core.Services.Monitoring.Stats;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Bus {
	/// <summary>
	/// Lightweight in-memory queue with a separate thread in which it passes messages
	/// to the consumer. It also tracks statistics about the message processing to help
	/// in identifying bottlenecks
	/// </summary>
	public class QueuedHandlerThreadPool : IQueuedHandler, IMonitoredQueue, IThreadSafePublisher {
		private static readonly ILogger Log = Serilog.Log.ForContext<QueuedHandlerThreadPool>();

		public int MessageCount {
			get { return _queue.Count; }
		}

		public string Name {
			get { return _queueStats.Name; }
		}

		private readonly IHandle<Message> _consumer;

		private readonly bool _watchSlowMsg;
		private readonly TimeSpan _slowMsgThreshold;

		private readonly ConcurrentQueueWrapper<QueueItem> _queue = new ConcurrentQueueWrapper<QueueItem>();

		private volatile bool _stop;
		private readonly ManualResetEventSlim _stopped = new ManualResetEventSlim(true);
		private readonly TimeSpan _threadStopWaitTimeout;

		// monitoring
		private readonly QueueMonitor _queueMonitor;
		private readonly QueueStatsCollector _queueStats;
		private readonly QueueTracker _tracker;

		private int _isRunning;
		private int _queueStatsState; //0 - never started, 1 - started, 2 - stopped

		private readonly TaskCompletionSource<object> _tcs = new TaskCompletionSource<object>();


		public QueuedHandlerThreadPool(IHandle<Message> consumer,
			string name,
			QueueStatsManager queueStatsManager,
			QueueTrackers trackers,
			bool watchSlowMsg = true,
			TimeSpan? slowMsgThreshold = null,
			TimeSpan? threadStopWaitTimeout = null,
			string groupName = null) {
			Ensure.NotNull(consumer, "consumer");
			Ensure.NotNull(name, "name");

			_consumer = consumer;

			_watchSlowMsg = watchSlowMsg;
			_slowMsgThreshold = slowMsgThreshold ?? InMemoryBus.DefaultSlowMessageThreshold;
			_threadStopWaitTimeout = threadStopWaitTimeout ?? QueuedHandler.DefaultStopWaitTimeout;

			_queueMonitor = QueueMonitor.Default;
			_queueStats = queueStatsManager.CreateQueueStatsCollector(name, groupName);
			_tracker = trackers.GetTrackerForQueue(name);
		}

		public Task Start() {
			_queueMonitor.Register(this);
			return _tcs.Task;
		}

		public void Stop() {
			_stop = true;
			if (!_stopped.Wait(_threadStopWaitTimeout))
				throw new TimeoutException(string.Format("Unable to stop thread '{0}'.", Name));
			TryStopQueueStats();
			_queueMonitor.Unregister(this);
		}

		public void RequestStop() {
			_stop = true;
			TryStopQueueStats();
			_queueMonitor.Unregister(this);
		}

		private void TryStopQueueStats() {
			if (Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0) {
				if (Interlocked.CompareExchange(ref _queueStatsState, 2, 1) == 1)
					_queueStats.Stop();
				Interlocked.CompareExchange(ref _isRunning, 0, 1);
			}
		}

		private void ReadFromQueue(object o) {
			try {
				if (Interlocked.CompareExchange(ref _queueStatsState, 1, 0) == 0)
					_queueStats.Start();

				bool proceed = true;
				while (proceed) {
					_stopped.Reset();
					_queueStats.EnterBusy();
					_tracker.EnterBusy();

					Message msg;
					while (!_stop && _queue.TryDequeue(out var item)) {
						var start = _tracker.RecordMessageDequeued(item.EnqueuedAt);
						msg = item.Message;
#if DEBUG
						_queueStats.Dequeued(msg);
#endif
						try {
							var queueCnt = _queue.Count;
							_queueStats.ProcessingStarted(msg.GetType(), queueCnt);

							if (_watchSlowMsg) {
								_consumer.Handle(msg);

								var end = _tracker.RecordMessageProcessed(start, msg.Label);
								var elapsed = TimeSpan.FromSeconds(end.ElapsedSecondsSince(start));

								if (elapsed > _slowMsgThreshold) {
									Log.Debug(
										"SLOW QUEUE MSG [{queue}]: {message} - {elapsed}ms. Q: {prevQueueCount}/{curQueueCount}. {messageDetail}.",
										_queueStats.Name, _queueStats.InProgressMessage.Name,
										(int)elapsed.TotalMilliseconds, queueCnt, _queue.Count, msg);
									if (elapsed > QueuedHandler.VerySlowMsgThreshold &&
										!(msg is SystemMessage.SystemInit))
										Log.Error(
											"---!!! VERY SLOW QUEUE MSG [{queue}]: {message} - {elapsed}ms. Q: {prevQueueCount}/{curQueueCount}.",
											_queueStats.Name, _queueStats.InProgressMessage.Name,
											(int)elapsed.TotalMilliseconds, queueCnt, _queue.Count);
								}
							} else {
								_consumer.Handle(msg);
								_tracker.RecordMessageProcessed(start, msg.Label);
							}

							_queueStats.ProcessingEnded(1);
						} catch (Exception ex) {
							Log.Error(ex,
								"Error while processing message {message} in queued handler '{queue}'.", msg,
								_queueStats.Name);
#if DEBUG
							throw;
#endif
						}
					}

					_queueStats.EnterIdle();
					_tracker.EnterIdle();
					Interlocked.CompareExchange(ref _isRunning, 0, 1);
					if (_stop) {
						TryStopQueueStats();
					}

					_stopped.Set();

					// try to reacquire lock if needed
					proceed = !_stop && _queue.Count > 0 && Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0;
				}
			} catch (Exception ex) {
				_tcs.TrySetException(ex);
				throw;
			}
		}

		public void Publish(Message message) {
			//Ensure.NotNull(message, "message");
#if DEBUG
			_queueStats.Enqueued();
#endif
			_queue.Enqueue(new(_tracker.Now, message));
			if (!_stop && Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0)
				ThreadPool.QueueUserWorkItem(ReadFromQueue);
		}

		public void Handle(Message message) {
			Publish(message);
		}

		public QueueStats GetStatistics() {
			return _queueStats.GetStatistics(_queue.Count);
		}
	}
}
