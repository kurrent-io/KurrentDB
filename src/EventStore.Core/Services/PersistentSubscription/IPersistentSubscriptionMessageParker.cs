// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Core.Messages;

namespace EventStore.Core.Services.PersistentSubscription;

public interface IPersistentSubscriptionMessageParker {
	void BeginParkMessage(ResolvedEvent ev, string reason, Action<ResolvedEvent, OperationResult> completed);
	void BeginReadEndSequence(Action<long?> completed);
	void BeginMarkParkedMessagesReprocessed(long sequence, DateTime? oldestParkedMessageTimestamp, bool updateOldestParkedMessage);
	void BeginDelete(Action<IPersistentSubscriptionMessageParker> completed);
	long ParkedMessageCount { get; }
	public void BeginLoadStats(Action completed);
	DateTime? GetOldestParkedMessage { get; }
}
