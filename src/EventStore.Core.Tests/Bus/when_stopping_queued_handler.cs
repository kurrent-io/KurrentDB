using System;
using System.Threading;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Bus.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Bus {
	[TestFixture]
	public abstract class when_stopping_queued_handler : QueuedHandlerTestWithNoopConsumer {
		protected when_stopping_queued_handler(
			Func<IHandle<Message>, string, TimeSpan, IQueuedHandler> queuedHandlerFactory)
			: base(queuedHandlerFactory) {
		}


		[Test]
		public void gracefully_should_not_throw() {
			Queue.Start();
			Assert.DoesNotThrow(() => Queue.Stop());
		}

		[Test]
		public void gracefully_and_queue_is_not_busy_should_not_take_much_time() {
			Queue.Start();

			var wait = new ManualResetEventSlim(false);

			ThreadPool.QueueUserWorkItem(_ => {
				Queue.Stop();
				wait.Set();
			});

			Assert.IsTrue(wait.Wait(5000), "Could not stop queue in time.");
		}

		[Test]
		public void second_time_should_not_throw() {
			Queue.Start();
			Queue.Stop();
			Assert.DoesNotThrow(() => Queue.Stop());
		}

		[Test]
		public void second_time_should_not_take_much_time() {
			Queue.Start();
			Queue.Stop();

			var wait = new ManualResetEventSlim(false);

			ThreadPool.QueueUserWorkItem(_ => {
				Queue.Stop();
				wait.Set();
			});

			Assert.IsTrue(wait.Wait(1000), "Could not stop queue in time.");
		}

		[Test]
		public void while_queue_is_busy_should_crash_with_timeout() {
			var consumer = new WaitingConsumer(1);
			var busyQueue = QueuedHandler.CreateQueuedHandler(consumer, "busy_test_queue", new QueueStatsManager(), new(), watchSlowMsg: false,
				threadStopWaitTimeout: TimeSpan.FromMilliseconds(100));
			var waitHandle = new ManualResetEvent(false);
			var handledEvent = new ManualResetEvent(false);
			try {
				busyQueue.Start();
				busyQueue.Publish(new DeferredExecutionTestMessage(() => {
					handledEvent.Set();
					waitHandle.WaitOne();
				}));

				handledEvent.WaitOne();
				Assert.Throws<TimeoutException>(() => busyQueue.Stop());
			} finally {
				waitHandle.Set();
				consumer.Wait();

				busyQueue.Stop();
				waitHandle.Dispose();
				handledEvent.Dispose();
				consumer.Dispose();
			}
		}
	}

	[TestFixture]
	public class when_stopping_queued_handler_mres_should : when_stopping_queued_handler {
		public when_stopping_queued_handler_mres_should()
			: base((consumer, name, timeout) => new QueuedHandlerMresWithMpsc(consumer, name, new QueueStatsManager(), false, null, timeout)) {
		}
	}

	[TestFixture]
	public class when_stopping_queued_handler_autoreset : when_stopping_queued_handler {
		public when_stopping_queued_handler_autoreset()
			: base((consumer, name, timeout) => new QueuedHandlerAutoResetWithMpsc(consumer, name, new QueueStatsManager(), false, null, timeout)
			) {
		}
	}

	[TestFixture]
	public class when_stopping_queued_handler_sleep : when_stopping_queued_handler {
		public when_stopping_queued_handler_sleep()
			: base((consumer, name, timeout) => new QueuedHandlerSleep(consumer, name, new QueueStatsManager(), false, null, timeout)) {
		}
	}

	[TestFixture]
	public class when_stopping_queued_handler_pulse : when_stopping_queued_handler {
		public when_stopping_queued_handler_pulse()
			: base((consumer, name, timeout) => new QueuedHandlerPulse(consumer, name, new QueueStatsManager(), false, null, timeout)) {
		}
	}

	[TestFixture]
	public class when_stopping_queued_handler_threadpool : when_stopping_queued_handler {
		public when_stopping_queued_handler_threadpool()
			: base((consumer, name, timeout) => new QueuedHandlerThreadPool(consumer, name, new QueueStatsManager(), new(), false, null, timeout)) {
		}
	}
}
