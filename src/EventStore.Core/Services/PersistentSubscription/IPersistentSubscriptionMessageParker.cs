// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Core.Messages;

namespace EventStore.Core.Services.PersistentSubscription;

public interface IPersistentSubscriptionMessageParker {
	void BeginParkMessage(ResolvedEvent ev, string reason, ParkReason parkReason, Action<ResolvedEvent, OperationResult> completed);
	void BeginReadEndSequence(Action<long?> completed);
	void BeginMarkParkedMessagesReprocessed(long sequence, DateTime? oldestParkedMessageTimestamp, bool updateOldestParkedMessage);
	void BeginDelete(Action<IPersistentSubscriptionMessageParker> completed);
	long ParkedMessageCount { get; }
	public void BeginLoadStats(Action completed);
	DateTime? GetOldestParkedMessage { get; }
	long ParkedDueToClientNak { get; }
	long ParkedDueToMaxRetries { get; }
	long ParkedMessageReplays { get; }
}

public enum ParkReason {
	ClientNak,
	MaxRetries,
}
