// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Client;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.Tests.Generators;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.gRPC;

public class gRPCMessageBatchAppender(KurrentDBClient client) : IMessageBatchAppender {
	public async ValueTask Append(TestMessageBatch batch) {
		bool appended = false;
		do {
			try {
				await client.AppendToStreamAsync(
					batch.StreamName,
					StreamState.Any,
					batch.Messages.Select(ToEventData)
				);
			} catch {
				await Task.Delay(10);
				continue;
			}

			appended = true;
		} while (!appended);
	}

	private static EventData ToEventData(TestMessageData messageData) =>
		new(Uuid.NewUuid(), messageData.EventType, messageData.Data, messageData.MetaData, "application/octet-stream");

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
