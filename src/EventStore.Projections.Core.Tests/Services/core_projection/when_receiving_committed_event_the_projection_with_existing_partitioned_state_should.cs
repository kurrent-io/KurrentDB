// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Text;
using EventStore.Core.Data;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;
using EventStore.Projections.Core.Services;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_receiving_committed_event_the_projection_with_existing_partitioned_state_should<TLogFormat, TStreamId> :
	TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	private Guid _eventId;
	private string _testProjectionState = @"{""test"":1}";

	protected override void Given() {
		_configureBuilderByQuerySource = source => {
			source.FromAll();
			source.AllEvents();
			source.SetByStream();
		};
		TicksAreHandledImmediately();
		NoStream("$projections-projection-result");
		NoStream("$projections-projection-order");
		AllWritesToSucceed("$projections-projection-order");
		ExistingEvent(
			"$projections-projection-partitions", "PartitionCreated",
			@"{""c"": 100, ""p"": 50}", "account-01");
		ExistingEvent(
			"$projections-projection-account-01-result", "Result",
			@"{""c"": 100, ""p"": 50}", _testProjectionState);
		ExistingEvent(
			"$projections-projection-checkpoint", ProjectionEventTypes.ProjectionCheckpoint,
			@"{""c"": 100, ""p"": 50}", _testProjectionState);
		AllWritesSucceed();
	}

	protected override void When() {
		//projection subscribes here
		_eventId = Guid.NewGuid();
		_consumer.HandledMessages.Clear();
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"account-01", 2, "account-01", 2, false, new TFPos(120, 110), _eventId,
					"handle_this_type", false, "data1", "metadata"), _subscriptionId, 0));
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"account-01", 3, "account-01", 3, false, new TFPos(160, 150), _eventId, "append", false,
					"$", "metadata"),
				_subscriptionId, 1));
	}

	[Test]
	public void register_new_partition_state_stream_only_once() {
		var writes =
			_writeEventHandler.HandledMessages.Where(v => v.EventStreamId == "$projections-projection-partitions")
				.ToArray();
		Assert.AreEqual(0, writes.Length);
	}
}
