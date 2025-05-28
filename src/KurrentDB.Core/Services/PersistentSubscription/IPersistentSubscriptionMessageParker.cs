// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;

namespace KurrentDB.Core.Services.PersistentSubscription;

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
	None = 0,
	ClientNak,
	MaxRetries,
}
