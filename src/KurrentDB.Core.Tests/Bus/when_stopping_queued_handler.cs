// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Tests.Bus.Helpers;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Bus;

[TestFixture]
public abstract class when_stopping_queued_handler : QueuedHandlerTestWithNoopConsumer {
	protected when_stopping_queued_handler(
		Func<IHandle<Message>, string, TimeSpan, IQueuedHandler> queuedHandlerFactory)
		: base(queuedHandlerFactory) {
	}


	[Test]
	public void gracefully_should_not_throw() {
		Queue.Start();
		Assert.DoesNotThrowAsync(Queue.Stop);
	}

	[Test]
	public async Task gracefully_and_queue_is_not_busy_should_not_take_much_time() {
		Queue.Start();
		await Queue.Stop().WaitAsync(TimeSpan.FromMilliseconds(5000));
	}

	[Test]
	public void second_time_should_not_throw() {
		Queue.Start();
		Assert.DoesNotThrowAsync(Queue.Stop);
	}

	[Test]
	public async Task second_time_should_not_take_much_time() {
		Queue.Start();
		await Queue.Stop().WaitAsync(TimeSpan.FromMilliseconds(5000))
			.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);

		Assert.IsTrue(Queue.Stop().IsCompletedSuccessfully);
	}
}

[TestFixture]
public class when_stopping_ThreadPoolMessageScheduler : when_stopping_queued_handler {
	public when_stopping_ThreadPoolMessageScheduler()
		: base(static (consumer, name, timeout) =>
			new ThreadPoolMessageScheduler(name, consumer)
				{ StopTimeout = timeout, SynchronizeMessagesWithUnknownAffinity = true }) {
	}
}
