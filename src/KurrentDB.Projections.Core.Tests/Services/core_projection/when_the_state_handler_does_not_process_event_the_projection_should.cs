// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Data;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using NUnit.Framework;
using ResolvedEvent = KurrentDB.Projections.Core.Services.Processing.ResolvedEvent;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_the_state_handler_does_not_process_event_the_projection_should<TLogFormat, TStreamId>
	: TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	protected override void Given() {
		ExistingEvent("$projections-projection-result", "Result", """{"c": 100, "p": 50}""", "{}");
		ExistingEvent("$projections-projection-checkpoint", ProjectionEventTypes.ProjectionCheckpoint, """{"c": 100, "p": 50}""", "{}");
	}

	protected override void When() {
		//projection subscribes here
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"/event_category/1", -1, "/event_category/1", -1, false, new TFPos(120, 110),
					new TFPos(120, 110),
					Guid.NewGuid(), "skip_this_type", false, [], [], null, null,
					default(DateTime)),
				SubscriptionId, 0));
	}

	[Test]
	public void not_update_state_snapshot() {
		Assert.AreEqual(0, _writeEventHandler.HandledMessages.Count);
	}
}
