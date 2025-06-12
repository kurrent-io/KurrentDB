// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.InMemory;

public class InMemoryLoadTestEnvironment: ILoadTestEnvironment {
	public IMessageBatchAppender MessageBatchAppender { get; } = new PublisherBasedMessageBatchAppender(new DummyPublisher());
	public ValueTask InitializeAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

	public ValueTask DisposeAsync() {
		return MessageBatchAppender.DisposeAsync();
	}
}
