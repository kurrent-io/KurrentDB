// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Bus.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Bus;

[TestFixture]
public abstract class queued_handler_should : QueuedHandlerTestWithNoopConsumer {
	protected queued_handler_should(Func<IHandle<Message>, string, TimeSpan, IQueuedHandler> queuedHandlerFactory)
		: base(queuedHandlerFactory) {
	}

	[Test]
	public void throw_if_handler_is_null() {
		Assert.Throws<ArgumentNullException>(
			static () => new QueuedHandlerThreadPool(null, "throwing", new(), new(), watchSlowMsg: false));
	}

	[Test]
	public void throw_if_name_is_null() {
		Assert.Throws<ArgumentNullException>(
			() => new QueuedHandlerThreadPool(Consumer, null, new(), new(), watchSlowMsg: false));
	}
}

[TestFixture]
public class queued_handler_threadpool_should : queued_handler_should {
	public queued_handler_threadpool_should()
		: base((consumer, name, timeout) =>
			new QueuedHandlerThreadPool(consumer, name, new QueueStatsManager(), new(), false, null, timeout)) {
	}
}
