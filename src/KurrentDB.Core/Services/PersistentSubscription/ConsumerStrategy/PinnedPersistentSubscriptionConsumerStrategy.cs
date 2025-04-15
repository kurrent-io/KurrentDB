// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Index.Hashes;

namespace KurrentDB.Core.Services.PersistentSubscription.ConsumerStrategy;

public class PinnedPersistentSubscriptionConsumerStrategy : PinnablePersistentSubscriptionConsumerStrategy {
	public PinnedPersistentSubscriptionConsumerStrategy(IHasher<string> streamHasher) : base(streamHasher) {
	}

	public override string Name {
		get { return SystemConsumerStrategies.Pinned; }
	}

	protected override string GetAssignmentSourceId(ResolvedEvent ev) {
		return GetSourceStreamId(ev);
	}
}
