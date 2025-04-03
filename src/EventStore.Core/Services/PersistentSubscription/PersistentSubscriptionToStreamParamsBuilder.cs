// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;

namespace EventStore.Core.Services.PersistentSubscription;

/// <summary>
/// Builds a <see cref="PersistentSubscriptionParams"/> object.
/// </summary>
public class PersistentSubscriptionToStreamParamsBuilder : PersistentSubscriptionParamsBuilder {
	/// <summary>
	/// Creates a new <see cref="PersistentSubscriptionParamsBuilder"></see> object
	/// </summary>
	/// <param name="streamName">The name of the stream for the subscription</param>
	/// <param name="groupName">The name of the group of the subscription</param>
	/// <returns>a new <see cref="PersistentSubscriptionParamsBuilder"></see> object</returns>
	public static PersistentSubscriptionParamsBuilder CreateFor(string streamName, string groupName) {
		return new PersistentSubscriptionToStreamParamsBuilder()
			.FromStream(streamName)
			.StartFrom(0)
			.SetGroup(groupName)
			.SetSubscriptionId(streamName + ":" + groupName)
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

	public PersistentSubscriptionToStreamParamsBuilder FromStream(string stream) {
		WithEventSource(new PersistentSubscriptionSingleStreamEventSource(stream));
		return this;
	}

	public PersistentSubscriptionToStreamParamsBuilder StartFrom(long startFrom) {
		StartFrom(new PersistentSubscriptionSingleStreamPosition(startFrom));
		return this;
	}

	public override PersistentSubscriptionParamsBuilder StartFromBeginning() {
		StartFrom(new PersistentSubscriptionSingleStreamPosition(0));
		return this;
	}

	public override PersistentSubscriptionParamsBuilder StartFromCurrent() {
		StartFrom(new PersistentSubscriptionSingleStreamPosition(-1));
		return this;
	}

}
