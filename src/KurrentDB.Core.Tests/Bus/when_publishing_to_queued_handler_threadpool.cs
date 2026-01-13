// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Tests.Bus;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Bus;

[TestFixture, Category("LongRunning")]
public class when_publishing_to_queued_handler_threadpool : when_publishing_to_queued_handler {
	public when_publishing_to_queued_handler_threadpool()
		: base(static (consumer, name, timeout) =>
			new QueuedHandlerThreadPool(consumer, name, new QueueStatsManager(), new(), _ => TimeSpan.Zero, timeout)) {
	}
}
