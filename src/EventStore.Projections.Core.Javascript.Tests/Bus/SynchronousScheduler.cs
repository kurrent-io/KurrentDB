// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Messaging;

namespace EventStore.Core.Bus;

public sealed class SynchronousScheduler(string name = "Test", bool watchSlowMsg = true) : InMemoryBus(name, watchSlowMsg), IPublisher {
	public void Publish(Message msg) {
		ArgumentNullException.ThrowIfNull(msg);

		var task = DispatchAsync(msg, CancellationToken.None);
		if (task.IsCompleted) {
			task.GetAwaiter().GetResult();
		} else {
			var wrapperTask = task.AsTask();

			try {
				wrapperTask.Wait();
			} catch (AggregateException e) when (e.InnerExceptions is [var innerEx]) {
				throw innerEx;
			} finally {
				wrapperTask.Dispose();
			}
		}
	}

	ValueTask IAsyncHandle<Message>.HandleAsync(Message msg, CancellationToken token)
		=> DispatchAsync(msg, token);
}
