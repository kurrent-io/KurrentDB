// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.LoadTesting.Generators;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.TestServer;

public class TestServerMessageBatchAppender(SecondaryIndexingEnabledFixture fixture) : IMessageBatchAppender {
	public async ValueTask Append(MessageBatch batch) =>
		await fixture.AppendToStream(batch.StreamName, batch.Messages.Select(ToEventData).ToArray());

	private static Event ToEventData(MessageData messageData) =>
		new(Guid.NewGuid(), messageData.EventType, false, messageData.Data, null, null);

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
