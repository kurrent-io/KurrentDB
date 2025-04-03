// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Services.Processing.Partitioning;

public class ByHandleStatePartitionSelector : StatePartitionSelector {
	private readonly IProjectionStateHandler _handler;

	public ByHandleStatePartitionSelector(IProjectionStateHandler handler) {
		_handler = handler;
	}

	public override string GetStatePartition(EventReaderSubscriptionMessage.CommittedEventReceived @event) {
		return _handler.GetStatePartition(@event.CheckpointTag, @event.EventCategory, @event.Data);
	}

	public override bool EventReaderBasePartitionDeletedIsSupported() {
		return false;
	}
}
