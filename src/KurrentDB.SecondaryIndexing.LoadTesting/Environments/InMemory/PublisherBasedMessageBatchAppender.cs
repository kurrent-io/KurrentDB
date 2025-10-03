// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.ClientPublisher;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.Tests.Generators;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.InMemory;

public class PublisherBasedMessageBatchAppender(IPublisher publisher) : IMessageBatchAppender {
	public async ValueTask Append(TestMessageBatch batch) {
		await publisher.WriteEvents(batch.StreamName, batch.Messages.Select(m => m.ToEventData()).ToArray());
	}

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
