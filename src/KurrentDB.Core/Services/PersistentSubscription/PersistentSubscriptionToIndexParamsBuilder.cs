// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Services.PersistentSubscription.ConsumerStrategy;

namespace KurrentDB.Core.Services.PersistentSubscription;

/// <summary>
/// Builds a <see cref="PersistentSubscriptionParams"/> object for index subscriptions.
/// </summary>
public class PersistentSubscriptionToIndexParamsBuilder : PersistentSubscriptionParamsBuilder {
	/// <summary>
	/// Creates a new <see cref="PersistentSubscriptionParamsBuilder"></see> object
	/// </summary>
	/// <param name="groupName">The name of the group of the subscription</param>
	/// <param name="indexName">The name of the index for the subscription</param>
	/// <returns>a new <see cref="PersistentSubscriptionParamsBuilder"></see> object</returns>
	public static PersistentSubscriptionParamsBuilder CreateFor(string groupName, string indexName) {
		return new PersistentSubscriptionToIndexParamsBuilder()
			.FromIndex(indexName)
			.StartFrom(0L, 0L)
			.SetGroup(groupName)
			.SetSubscriptionId($"$index-{indexName}::{groupName}")
			.DoNotResolveLinkTos()
			.WithMessageTimeoutOf(TimeSpan.FromSeconds(30))
			.WithHistoryBufferSizeOf(500)
			.WithLiveBufferSizeOf(500)
			.WithMaxRetriesOf(10)
			.WithReadBatchOf(20)
			.CheckPointAfter(TimeSpan.FromSeconds(1))
			.MinimumToCheckPoint(5)
			.MaximumToCheckPoint(1000)
			.MaximumSubscribers(0)
			.WithNamedConsumerStrategy(new RoundRobinPersistentSubscriptionConsumerStrategy());
	}

	public PersistentSubscriptionToIndexParamsBuilder FromIndex(string indexName) {
		WithEventSource(new PersistentSubscriptionIndexEventSource(indexName));
		return this;
	}

	public PersistentSubscriptionToIndexParamsBuilder StartFrom(long commitPosition, long preparePosition) {
		StartFrom(new PersistentSubscriptionAllStreamPosition(commitPosition, preparePosition));
		return this;
	}

	public override PersistentSubscriptionParamsBuilder StartFromBeginning() {
		StartFrom(new PersistentSubscriptionAllStreamPosition(0, 0));
		return this;
	}

	public override PersistentSubscriptionParamsBuilder StartFromCurrent() {
		StartFrom(new PersistentSubscriptionAllStreamPosition(-1, -1));
		return this;
	}
}
