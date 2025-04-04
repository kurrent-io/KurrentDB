using System;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Bus.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Bus {
	[TestFixture, Category("bus")]
	public abstract class when_consumer_throws : QueuedHandlerTestWithWaitingConsumer {
		protected when_consumer_throws(
			Func<IHandle<Message>, string, TimeSpan, IQueuedHandler> queuedHandlerFactory)
			: base(queuedHandlerFactory) {
		}

		public override void SetUp() {
			base.SetUp();
		}

		public override void TearDown() {
			Consumer.Dispose();
			Queue.Stop();
			base.TearDown();
		}

		[Test]
		public void all_messages_in_the_queue_should_be_delivered() {
#if DEBUG
			Assert.Ignore(
				"This test is not supported with DEBUG conditional since all exceptions are thrown in DEBUG builds.");
#else
            Consumer.SetWaitingCount(3);

            Queue.Publish(new TestMessage());
            Queue.Publish(new ExecutableTestMessage(() =>
            {
                throw new NullReferenceException();
            }));
            Queue.Publish(new TestMessage2());

            Queue.Start();
            Consumer.Wait();

            Assert.IsTrue(Consumer.HandledMessages.ContainsSingle<TestMessage>());
            Assert.IsTrue(Consumer.HandledMessages.ContainsSingle<ExecutableTestMessage>());
            Assert.IsTrue(Consumer.HandledMessages.ContainsSingle<TestMessage2>());
#endif
		}
	}

	[TestFixture, Category("bus")]
	public class when_consumer_throws_mres : when_consumer_throws {
		public when_consumer_throws_mres()
			: base((consumer, name, timeout) => new QueuedHandlerMresWithMpsc(consumer, name, new QueueStatsManager(), false, null, timeout)) {
		}
	}

	[TestFixture, Category("bus")]
	public class when_consumer_throws_autoreset : when_consumer_throws {
		public when_consumer_throws_autoreset()
			: base((consumer, name, timeout) =>
				new QueuedHandlerAutoResetWithMpsc(consumer, name, new QueueStatsManager(), false, null, timeout)) {
		}
	}
}
