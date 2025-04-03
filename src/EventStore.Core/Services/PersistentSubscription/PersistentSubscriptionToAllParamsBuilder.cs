// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;
using EventStore.Core.Services.Storage.ReaderIndex;

namespace EventStore.Core.Services.PersistentSubscription;

/// <summary>
/// Builds a <see cref="PersistentSubscriptionParams"/> object.
/// </summary>
public class PersistentSubscriptionToAllParamsBuilder : PersistentSubscriptionParamsBuilder {
	/// <summary>
	/// Creates a new <see cref="PersistentSubscriptionParamsBuilder"></see> object
	/// </summary>
	/// <param name="groupName">The name of the group of the subscription</param>
	/// <param name="filter">The optional filter for the subscription</param>
	/// <returns>a new <see cref="PersistentSubscriptionParamsBuilder"></see> object</returns>
	public static PersistentSubscriptionParamsBuilder CreateFor(string groupName, IEventFilter filter = null) {
		return new PersistentSubscriptionToAllParamsBuilder()
			.FromAll(filter)
			.StartFrom(0L, 0L)
			.SetGroup(groupName)
			.SetSubscriptionId("$all" + ":" + groupName)
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

	public PersistentSubscriptionToAllParamsBuilder FromAll(IEventFilter filter = null) {
		WithEventSource(new PersistentSubscriptionAllStreamEventSource(filter));
		return this;
	}

	public PersistentSubscriptionToAllParamsBuilder StartFrom(long commitPosition, long preparePosition) {
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
