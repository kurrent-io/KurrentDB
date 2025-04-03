// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_creating_a_new_partitiion_the_projection_should<TLogFormat, TStreamId> : TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	private Guid _eventId;

	protected override void Given() {
		_configureBuilderByQuerySource = source => {
			source.FromAll();
			source.AllEvents();
			source.SetByStream();
			source.SetDefinesStateTransform();
		};
		TicksAreHandledImmediately();
		AllWritesSucceed();
		NoOtherStreams();
	}

	protected override void When() {
		//projection subscribes here
		_eventId = Guid.NewGuid();
		_consumer.HandledMessages.Clear();
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"account-01", -1, "account-01", -1, false, new TFPos(120, 110), _eventId, "handle_this_type",
					false, "data", "metadata"), _subscriptionId, 0));
	}

	[Test]
	public void passes_partition_created_notification_to_the_handler() {
		Assert.AreEqual(1, _stateHandler._partitionCreatedProcessed);
		Assert.Inconclusive();
	}
}
