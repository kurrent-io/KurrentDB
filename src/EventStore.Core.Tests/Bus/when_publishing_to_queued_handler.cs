// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Bus.Helpers;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Bus;

[TestFixture, Category("LongRunning")]
public abstract class when_publishing_to_queued_handler : QueuedHandlerTestWithWaitingConsumer {
	protected when_publishing_to_queued_handler(
		Func<IHandle<Message>, string, TimeSpan, IQueuedHandler> queuedHandlerFactory)
		: base(queuedHandlerFactory) {
	}

	public override void SetUp() {
		base.SetUp();
		Queue.Start();
	}

	public override async Task TearDown() {
		Consumer.Dispose();
		await Queue.Stop();
		await base.TearDown();
	}

	[Test, Ignore("We do not check each message for null for performance reasons.")]
	public void null_message_should_throw() {
		Assert.Throws<ArgumentNullException>(() => Queue.Publish(null));
	}

	[Test]
	public void message_it_should_be_delivered_to_bus() {
		Consumer.SetWaitingCount(1);

		Queue.Publish(new TestMessage());

		Consumer.Wait();
		Assert.IsTrue(Consumer.HandledMessages.ContainsSingle<TestMessage>());
	}

	[Test]
	public void multiple_messages_they_should_be_delivered_to_bus() {
		Consumer.SetWaitingCount(2);

		Queue.Publish(new TestMessage());
		Queue.Publish(new TestMessage2());

		Consumer.Wait();

		Assert.IsTrue(Consumer.HandledMessages.ContainsSingle<TestMessage>());
		Assert.IsTrue(Consumer.HandledMessages.ContainsSingle<TestMessage2>());
	}

	[Test]
	public void messages_order_should_remain_the_same() {
		Consumer.SetWaitingCount(6);

		Queue.Publish(new TestMessageWithId(4));
		Queue.Publish(new TestMessageWithId(8));
		Queue.Publish(new TestMessageWithId(15));
		Queue.Publish(new TestMessageWithId(16));
		Queue.Publish(new TestMessageWithId(23));
		Queue.Publish(new TestMessageWithId(42));

		Consumer.Wait();

		var typedMessages = Consumer.HandledMessages.OfType<TestMessageWithId>().ToArray();
		Assert.AreEqual(6, typedMessages.Length);
		Assert.AreEqual(4, typedMessages[0].Id);
		Assert.AreEqual(8, typedMessages[1].Id);
		Assert.AreEqual(15, typedMessages[2].Id);
		Assert.AreEqual(16, typedMessages[3].Id);
		Assert.AreEqual(23, typedMessages[4].Id);
		Assert.AreEqual(42, typedMessages[5].Id);
	}
}

[TestFixture, Category("LongRunning")]
public class when_publishing_to_queued_handler_threadpool : when_publishing_to_queued_handler {
	public when_publishing_to_queued_handler_threadpool()
		: base((consumer, name, timeout) =>
			new QueuedHandlerThreadPool(consumer, name, new QueueStatsManager(), new(), false, null, timeout)) {
	}
}
