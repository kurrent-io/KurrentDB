// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Bus;

public sealed class ThreadPoolMessageSchedulerTests {
	public static TheoryData<ThreadPoolMessageScheduler.MessageProcessingStrategy> Strategies => [
		ThreadPoolMessageScheduler.SynchronizeMessagesWithUnknownAffinity(),
		ThreadPoolMessageScheduler.UseRateLimitForUnknownAffinity(1L),
	];

	[MemberData(nameof(Strategies))]
	[Theory]
	public static async Task CheckQueueLength(ThreadPoolMessageScheduler.MessageProcessingStrategy strategy) {
		const string queueName = "TestQueue";

		var handler = new DummyHandler();
		var scheduler = new ThreadPoolMessageScheduler(queueName, handler) {
			Strategy = strategy,
			StatsManager = new QueueStatsManager()
		};

		scheduler.Start();

		// the handler is suspended, but the queue is empty
		scheduler.Publish(new TestMessage());

		var stat = QueueMonitor
			.Default
			.GetStats()
			.FirstOrDefault(static stat => stat is { Name: queueName });

		Assert.NotNull(stat);
		Assert.Equal(0, stat.Length);

		scheduler.Publish(new TestMessage());
		stat = QueueMonitor
			.Default
			.GetStats()
			.FirstOrDefault(static stat => stat is { Name: queueName });

		Assert.NotNull(stat);
		Assert.Equal(1, stat.Length);

		handler.TrySetResult();
		await scheduler.Stop();
	}

	private sealed class DummyHandler : TaskCompletionSource, IAsyncHandle<Message> {
		public ValueTask HandleAsync(Message message, CancellationToken token)
			=> new(Task.WaitAsync(token));
	}

	private sealed class TestMessage : Message {
		public override object Affinity => UnknownAffinity;
	}
}
