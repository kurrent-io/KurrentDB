// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using NUnit.Framework;

namespace EventStore.Core.Tests.Bus.Helpers;

public abstract class QueuedHandlerTestWithWaitingConsumer {
	private readonly Func<IHandle<Message>, string, TimeSpan, IQueuedHandler> _queuedHandlerFactory;

	protected IQueuedHandler Queue;
	protected WaitingConsumer Consumer;

	protected QueuedHandlerTestWithWaitingConsumer(
		Func<IHandle<Message>, string, TimeSpan, IQueuedHandler> queuedHandlerFactory) {
		Ensure.NotNull(queuedHandlerFactory, "queuedHandlerFactory");
		_queuedHandlerFactory = queuedHandlerFactory;
	}

	[SetUp]
	public virtual void SetUp() {
		Consumer = new WaitingConsumer(0);
		Queue = _queuedHandlerFactory(Consumer, "waiting_queue", TimeSpan.FromMilliseconds(5000));
	}

	[TearDown]
	public virtual async Task TearDown() {
		await Queue.Stop();
		Queue = null;
		Consumer.Dispose();
		Consumer = null;
	}
}
