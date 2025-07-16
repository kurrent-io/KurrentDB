// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.InMemory;

public class DummyPublisher : IPublisher {
	public void Publish(Message message) {
		if (message is not ClientMessage.WriteEvents writeEvents)
			return;

		var reply = new ClientMessage.WriteEventsCompleted(
			Guid.NewGuid(),
			firstEventNumbers: new LowAllocReadOnlyMemory<long>(0),
			lastEventNumbers: new LowAllocReadOnlyMemory<long>(1),
			preparePosition: 1,
			commitPosition: 1
		);

		writeEvents.Envelope.ReplyWith(reply);
	}
}
