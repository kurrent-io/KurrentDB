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
	public class QueuedHandlerAutoResetWithMpsc : IQueuedHandler, IHandle<Message>, IPublisher, IMonitoredQueue,
		IThreadSafePublisher {
		private static readonly ILogger Log = Serilog.Log.ForContext<QueuedHandlerAutoResetWithMpsc>();

		public string Name {
			get { return _queueStats.Name; }
		}

		private readonly IHandle<Message> _consumer;

		private readonly bool _watchSlowMsg;
		private readonly TimeSpan _slowMsgThreshold;

		// assuming 8bytes per object ref its ~1MB.
		private readonly MPSCMessageQueue _queue = new MPSCMessageQueue(128 * 1024);

		private readonly AutoResetEvent _msgAddEvent = new AutoResetEvent(false);

		private Thread _thread;
		private volatile bool _stop;
		private volatile bool _starving;
		private readonly ManualResetEventSlim _stopped = new ManualResetEventSlim(true);
		private readonly TimeSpan _threadStopWaitTimeout;

		// monitoring
		private readonly QueueMonitor _queueMonitor;
		private readonly QueueStatsCollector _queueStats;
		private readonly TaskCompletionSource<object> _tcs = new TaskCompletionSource<object>();

		public QueuedHandlerAutoResetWithMpsc(IHandle<Message> consumer,
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

				var batch = new Message[128];
				while (!_stop) {
					Message msg = null;
					try {
						QueueBatchDequeueResult dequeueResult;
						if (_queue.TryDequeue(batch, out dequeueResult) == false) {
							_starving = true;

							_queueStats.EnterIdle();
							_msgAddEvent.WaitOne(100);
							_msgAddEvent.Reset();

							_starving = false;
						} else {
							var estimatedQueueCount = dequeueResult.EstimateCurrentQueueCount;

							for (var i = 0; i < dequeueResult.DequeueCount; i++) {
								try {
									msg = batch[i];


									_queueStats.EnterBusy();
#if DEBUG
									_queueStats.Dequeued(msg);
#endif

									_queueStats.ProcessingStarted(msg.GetType(), estimatedQueueCount);

									if (_watchSlowMsg) {
										var start = DateTime.UtcNow;

										_consumer.Handle(msg);

										var elapsed = DateTime.UtcNow - start;
										if (elapsed > _slowMsgThreshold) {
											Log.Debug(
												"SLOW QUEUE MSG [{queue}]: {message} - {elapsed}ms. Q: {prevEstimatedQueueCount}/{curEstimatedQueueCount}. {messageDetail}.",
												Name, _queueStats.InProgressMessage.Name,
												(int)elapsed.TotalMilliseconds,
												estimatedQueueCount,
												_queue.EstimageCurrentQueueCount(),
												msg);
											if (elapsed > QueuedHandler.VerySlowMsgThreshold &&
												!(msg is SystemMessage.SystemInit))
												Log.Error(
													"---!!! VERY SLOW QUEUE MSG [{queue}]: {message} - {elapsed}ms. Q: {prevEstimatedQueueCount}/{curEstimatedQueueCount}.",
													Name, _queueStats.InProgressMessage.Name,
													(int)elapsed.TotalMilliseconds,
													estimatedQueueCount, _queue.EstimageCurrentQueueCount());
										}
									} else {
										_consumer.Handle(msg);
									}
								} catch (Exception ex) {
									Log.Error(ex,
										"Error while processing message {message} in queued handler '{queue}'.", msg,
										Name);
#if DEBUG
									throw;
#endif
								}

								estimatedQueueCount -= 1;
								_queueStats.ProcessingEnded(1);
							}
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
			if (_starving)
				_msgAddEvent.Set();
		}

		public void Handle(Message message) {
			Publish(message);
		}

		public QueueStats GetStatistics() {
			return _queueStats.GetStatistics(_queue.EstimageCurrentQueueCount());
		}
	}
}
