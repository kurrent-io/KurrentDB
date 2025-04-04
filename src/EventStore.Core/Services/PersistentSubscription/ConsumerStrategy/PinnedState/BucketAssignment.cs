// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Services.PersistentSubscription.ConsumerStrategy.PinnedState;

internal struct BucketAssignment {
	internal enum BucketState {
		Unassigned,
		Assigned,
		Disconnected
	}

	public Guid NodeId { get; set; }

	public BucketState State { get; set; }

	public int InFlightCount { get; set; }

	public Node Node { get; set; }
}
