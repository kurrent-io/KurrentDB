// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using NUnit.Framework;

namespace EventStore.Core.Tests.Bus.Helpers;

public abstract class QueuedHandlerTestWithNoopConsumer {
	private readonly Func<IHandle<Message>, string, TimeSpan, IQueuedHandler> _queuedHandlerFactory;

	protected IQueuedHandler Queue;
	protected IHandle<Message> Consumer;

	protected QueuedHandlerTestWithNoopConsumer(
		Func<IHandle<Message>, string, TimeSpan, IQueuedHandler> queuedHandlerFactory) {
		Ensure.NotNull(queuedHandlerFactory, "queuedHandlerFactory");
		_queuedHandlerFactory = queuedHandlerFactory;
	}

	[SetUp]
	public virtual void SetUp() {
		Consumer = new NoopConsumer();
		Queue = _queuedHandlerFactory(Consumer, "test_name", TimeSpan.FromMilliseconds(5000));
	}

	[TearDown]
	public virtual async Task TearDown() {
		await Queue.Stop();
		Queue = null;
		Consumer = null;
	}
}
