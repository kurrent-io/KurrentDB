// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.LoadTesting.Generators;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Appenders;

public interface IMessageBatchAppender {
	ValueTask Append(MessageBatch batch);
}

public class PublisherBasedMessageBatchAppender(IPublisher publisher) : IMessageBatchAppender {
	public async ValueTask Append(MessageBatch batch) {
		await publisher.WriteEvents(batch.StreamName, batch.Messages.Select(ToEventData).ToArray());
	}

	private static Event ToEventData(MessageData messageData) =>
		new(Guid.NewGuid(), messageData.EventType, false, messageData.Data, null, null);
}
