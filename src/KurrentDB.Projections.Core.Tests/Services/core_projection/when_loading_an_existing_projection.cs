// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_loading_an_existing_projection<TLogFormat, TStreamId> : TestFixtureWithCoreProjectionLoaded<TLogFormat, TStreamId> {
	private const string TestProjectionState = """{"test":1}""";

	protected override void Given() {
		ExistingEvent(
			"$projections-projection-result", "Result",
			"""{"c": 100, "p": 50}""", TestProjectionState);
		ExistingEvent(
			"$projections-projection-checkpoint", ProjectionEventTypes.ProjectionCheckpoint,
			"""{"c": 100, "p": 50}""", TestProjectionState);
		ExistingEvent(
			"$projections-projection-result", "Result",
			"""{"c": 200, "p": 150}""", TestProjectionState);
		ExistingEvent(
			"$projections-projection-result", "Result",
			"""{"c": 300, "p": 250}""", TestProjectionState);
	}

	protected override void When() {
	}

	[Test]
	public void should_not_subscribe() {
		Assert.AreEqual(0, _subscribeProjectionHandler.HandledMessages.Count);
	}

	[Test]
	public void should_not_load_projection_state_handler() {
		Assert.AreEqual(0, _stateHandler._loadCalled);
	}

	[Test]
	public void should_not_publish_started_message() {
		Assert.AreEqual(0, _consumer.HandledMessages.OfType<CoreProjectionStatusMessage.Started>().Count());
	}
}
