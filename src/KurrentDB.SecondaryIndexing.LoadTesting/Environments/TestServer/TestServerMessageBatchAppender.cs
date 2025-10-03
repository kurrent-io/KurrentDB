// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using KurrentDB.SecondaryIndexing.Tests.Generators;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.TestServer;

public class TestServerMessageBatchAppender(SecondaryIndexingEnabledFixture fixture) : IMessageBatchAppender {
	public async ValueTask Append(TestMessageBatch batch) {
		bool appended = false;
		do {
			try {
				await fixture.AppendToStream(batch.StreamName, batch.Messages.Select(ToEventData).ToArray());
			} catch {
				await Task.Delay(10);
				continue;
			}
			appended = true;
		} while (!appended);
	}

	private static Event ToEventData(TestMessageData messageData) =>
		new(Guid.NewGuid(), messageData.EventType, false, messageData.Data, false, null);

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
