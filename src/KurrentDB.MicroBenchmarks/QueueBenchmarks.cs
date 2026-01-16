// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using BenchmarkDotNet.Attributes;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Metrics;

namespace KurrentDB.MicroBenchmarks;

[MemoryDiagnoser]
public class QueueBenchmarks {
	const int BatchSize = 100_000;
	private readonly ThreadPoolMessageScheduler _threadPoolMessageScheduler;
	private readonly QueuedHandlerThreadPool _queuedHandlerThreadPool;
	private readonly Message _message = new SystemMessage.BecomeLeader(Guid.NewGuid());
	private readonly CountdownEvent _countdown = new(BatchSize);

	public QueueBenchmarks() {
		var queueStatsManager = new QueueStatsManager();
		var queueTrackers = new QueueTrackers();
		var handler = new AdHocHandler<Message>(_ => {
			_countdown.Signal();
		});

		_queuedHandlerThreadPool = new QueuedHandlerThreadPool(
			handler,
			"QueuedHandlerThreadPool",
			queueStatsManager,
			queueTrackers,
			_ => TimeSpan.Zero);
		_queuedHandlerThreadPool.Start();

		_threadPoolMessageScheduler = new("ThreadPoolMessageScheduler", handler) {
			Strategy = Core.Bus.ThreadPoolMessageScheduler.SynchronizeMessagesWithUnknownAffinity(),
			Trackers = queueTrackers,
			StatsManager = queueStatsManager,
			MaxPoolSize = BatchSize,
		};
		_threadPoolMessageScheduler.Start();
	}

	[Benchmark(Baseline = true)]
	public void QueuedHandlerThreadPool() {
		_countdown.Reset();
		for (int i = 0; i < BatchSize; i++)
			_queuedHandlerThreadPool.Publish(_message);
		_countdown.Wait();
	}

	[Benchmark]
	public void ThreadPoolMessageScheduler() {
		_countdown.Reset();
		for (int i = 0; i < BatchSize; i++)
			_threadPoolMessageScheduler.Publish(_message);
		_countdown.Wait();
	}
}
