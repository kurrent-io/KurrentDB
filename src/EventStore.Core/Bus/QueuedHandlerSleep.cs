using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Monitoring.Stats;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Bus {
	/// <summary>
	/// Lightweight in-memory queue with a separate thread in which it passes messages
	/// to the consumer. It also tracks statistics about the message processing to help
	/// in identifying bottlenecks
	/// </summary>
	public class QueuedHandlerSleep : IQueuedHandler, IHandle<Message>, IPublisher, IMonitoredQueue,
		IThreadSafePublisher {
		private static readonly ILogger Log = Serilog.Log.ForContext<QueuedHandlerSleep>();

		public int MessageCount {
			get { return _queue.Count; }
		}

		public string Name {
			get { return _queueStats.Name; }
		}

		private readonly IHandle<Message> _consumer;

		private readonly bool _watchSlowMsg;
		private readonly TimeSpan _slowMsgThreshold;

		private readonly ConcurrentQueueWrapper<Message> _queue = new ConcurrentQueueWrapper<Message>();

		private Thread _thread;
		private volatile bool _stop;
		private readonly ManualResetEventSlim _stopped = new ManualResetEventSlim(true);
		private readonly TimeSpan _threadStopWaitTimeout;

		// monitoring
		private readonly QueueMonitor _queueMonitor;
		private readonly QueueStatsCollector _queueStats;
		private readonly TaskCompletionSource<object> _tcs = new TaskCompletionSource<object>();

		public QueuedHandlerSleep(IHandle<Message> consumer,
			string name,
			QueueStatsManager queueStatsManager,
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
		}

		public Task Start() {
			if (_thread != null)
				throw new InvalidOperationException("Already a thread running.");

			_queueMonitor.Register(this);

			_stopped.Reset();

			_thread = new Thread(ReadFromQueue) { IsBackground = true, Name = Name };
			_thread.Start();
			return _tcs.Task;
		}

		public void Stop() {
			_stop = true;
			if (!_stopped.Wait(_threadStopWaitTimeout))
				throw new TimeoutException(string.Format("Unable to stop thread '{0}'.", Name));
		}

		public void RequestStop() {
			_stop = true;
		}

		private void ReadFromQueue(object o) {
			try {
				_queueStats.Start();
				Thread.BeginThreadAffinity(); // ensure we are not switching between OS threads. Required at least for v8.

				const int spinmax = 5000;
				var iterationsCount = 0;
				while (!_stop) {
					Message msg = null;
					try {
						if (!_queue.TryDequeue(out msg)) {
							_queueStats.EnterIdle();

							iterationsCount += 1;
							if (iterationsCount < spinmax) {
								//do nothing... spin
							} else {
								Thread.Sleep(1);
							}
						} else {
							_queueStats.EnterBusy();
#if DEBUG
							_queueStats.Dequeued(msg);
#endif

							var cnt = _queue.Count;
							_queueStats.ProcessingStarted(msg.GetType(), cnt);

							if (_watchSlowMsg) {
								var start = DateTime.UtcNow;

								_consumer.Handle(msg);

								var elapsed = DateTime.UtcNow - start;
								if (elapsed > _slowMsgThreshold) {
									Log.Debug(
										"SLOW QUEUE MSG [{queue}]: {message} - {elapsed}ms. Q: {prevQueueCount}/{curQueueCount}. {messageDetail}.",
										Name, _queueStats.InProgressMessage.Name, (int)elapsed.TotalMilliseconds, cnt,
										_queue.Count, msg);
									if (elapsed > QueuedHandler.VerySlowMsgThreshold &&
										!(msg is SystemMessage.SystemInit))
										Log.Error(
											"---!!! VERY SLOW QUEUE MSG [{queue}]: {message} - {elapsed}ms. Q: {prevQueueCount}/{curQueueCount}.",
											Name, _queueStats.InProgressMessage.Name, (int)elapsed.TotalMilliseconds,
											cnt, _queue.Count);
								}
							} else {
								_consumer.Handle(msg);
							}

							_queueStats.ProcessingEnded(1);
						}
					} catch (Exception ex) {
						Log.Error(ex, "Error while processing message {message} in queued handler '{queue}'.",
							msg, Name);
#if DEBUG
						throw;
#endif
					}
				}
			} catch (Exception ex) {
				_tcs.TrySetException(ex);
				throw;
			} finally {
				_queueStats.Stop();

				_stopped.Set();
				_queueMonitor.Unregister(this);
				Thread.EndThreadAffinity();
			}
		}

		public void Publish(Message message) {
			//Ensure.NotNull(message, "message");
#if DEBUG
			_queueStats.Enqueued();
#endif
			_queue.Enqueue(message);
		}

		public void Handle(Message message) {
			Publish(message);
		}

		public QueueStats GetStatistics() {
			return _queueStats.GetStatistics(_queue.Count);
		}
	}
}
