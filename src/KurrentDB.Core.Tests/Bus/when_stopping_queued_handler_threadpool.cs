// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Tests.Bus;
using KurrentDB.Core.Tests.Bus.Helpers;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Bus;

[TestFixture]
public class when_stopping_queued_handler_threadpool : when_stopping_queued_handler {
	public when_stopping_queued_handler_threadpool()
		: base(static (consumer, name, timeout) =>
			new QueuedHandlerThreadPool(consumer, name, new QueueStatsManager(), new(), false, null, timeout)) {
	}

	[Test]
	public void while_queue_is_busy_should_crash_with_timeout() {
		var consumer = new WaitingConsumer(1);
		var busyQueue = new QueuedHandlerThreadPool(consumer, "busy_test_queue", new QueueStatsManager(), new(),
			watchSlowMsg: false,
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
			Assert.ThrowsAsync<TimeoutException>(busyQueue.Stop);
		} finally {
			waitHandle.Set();
			consumer.Wait();

			busyQueue.RequestStop();
			waitHandle.Dispose();
			handledEvent.Dispose();
			consumer.Dispose();
		}
	}
}
