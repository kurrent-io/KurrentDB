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
public class when_starting_an_existing_projection_and_an_event_is_received<TLogFormat, TStreamId>
	: TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	private const string TestProjectionState = """{"test":1}""";

	protected override void Given() {
		ExistingEvent("$projections-projection-result", "Result", """{"c": 100, "p": 50}""", TestProjectionState);
		ExistingEvent("$projections-projection-checkpoint", ProjectionEventTypes.ProjectionCheckpoint, """{"c": 100, "p": 50}""", TestProjectionState);
		ExistingEvent("$projections-projection-result", "Result", """{"c": 200, "p": 150}""", TestProjectionState);
		ExistingEvent("$projections-projection-result", "Result", """{"c": 300, "p": 250}""", TestProjectionState);
		NoStream("$projections-projection-order");
		AllWritesToSucceed("$projections-projection-order");
	}

	protected override void When() {
		var eventId = Guid.NewGuid();
		_bus.Publish(
			EventReaderSubscriptionMessage.CommittedEventReceived.Sample(
				new ResolvedEvent(
					"/event_category/1", -1, "/event_category/1", -1, false, new TFPos(120, 110), eventId,
					"append", false, "data", "metadata"), SubscriptionId, 0));
	}

	[Test]
	public void should_load_projection_state_handler() {
		Assert.AreEqual(1, _stateHandler._loadCalled);
		Assert.AreEqual(TestProjectionState + "data", _stateHandler._loadedState);
	}
}
