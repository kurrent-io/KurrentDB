// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Services.Processing.Partitioning;

public abstract class StatePartitionSelector {
	public abstract string GetStatePartition(EventReaderSubscriptionMessage.CommittedEventReceived @event);
	public abstract bool EventReaderBasePartitionDeletedIsSupported();
}
