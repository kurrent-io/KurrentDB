// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

public abstract class TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> : TestFixtureWithCoreProjection<TLogFormat, TStreamId> {
	protected Guid _subscriptionId;

	protected override void PreWhen() {
		_coreProjection.Start();
		var lastSubscribe =
			_consumer.HandledMessages.OfType<ReaderSubscriptionManagement.Subscribe>().LastOrDefault();
		_subscriptionId = lastSubscribe != null ? lastSubscribe.SubscriptionId : Guid.NewGuid();
		_bus.Publish(new EventReaderSubscriptionMessage.ReaderAssignedReader(_subscriptionId, Guid.NewGuid()));
	}
}
