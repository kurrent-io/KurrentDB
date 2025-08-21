// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Bus;

public class MultiQueuedHandler : IPublisher {
	private readonly ReadOnlyMemory<IQueuedHandler> _queues;
	private int _nextQueueNum = -1;

	public MultiQueuedHandler(int queueCount,
		Func<int, IQueuedHandler> queueFactory) {
		Ensure.Positive(queueCount, "queueCount");
		Ensure.NotNull(queueFactory, "queueFactory");

		var queues = new IQueuedHandler[queueCount];
		for (int i = 0; i < queues.Length; ++i) {
			queues[i] = queueFactory(i);
		}

		_queues = queues;
	}

	private int NextQueueHash() => Interlocked.Increment(ref _nextQueueNum);

	public void Start() {
		foreach (var t in _queues.Span) {
			t.Start();
		}
	}

	public Task Stop() {
		var stopTasks = new Task[_queues.Length];
		var queues = _queues.Span;
		for (int i = 0; i < queues.Length; ++i) {
			stopTasks[i] = Task.Run(queues[i].Stop);
		}

		return Task.WhenAll(stopTasks);
	}

	public void Publish(Message message) {
		var queueHash = message.Affinity is not { } affinity || ReferenceEquals(affinity, Message.UnknownAffinity)
			? NextQueueHash()
			: affinity.GetHashCode();
		var queueNum = (int)((uint)queueHash % _queues.Length);
		_queues.Span[queueNum].Publish(message);
	}

	public void PublishToAll(Message message) {
		foreach (var queue in _queues.Span) {
			queue.Publish(message);
		}
	}
}
