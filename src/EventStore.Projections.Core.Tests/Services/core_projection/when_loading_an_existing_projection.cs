// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;
using EventStore.Projections.Core.Services;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_loading_an_existing_projection<TLogFormat, TStreamId> : TestFixtureWithCoreProjectionLoaded<TLogFormat, TStreamId> {
	private string _testProjectionState = @"{""test"":1}";

	protected override void Given() {
		ExistingEvent(
			"$projections-projection-result", "Result",
			@"{""c"": 100, ""p"": 50}", _testProjectionState);
		ExistingEvent(
			"$projections-projection-checkpoint", ProjectionEventTypes.ProjectionCheckpoint,
			@"{""c"": 100, ""p"": 50}", _testProjectionState);
		ExistingEvent(
			"$projections-projection-result", "Result",
			@"{""c"": 200, ""p"": 150}", _testProjectionState);
		ExistingEvent(
			"$projections-projection-result", "Result",
			@"{""c"": 300, ""p"": 250}", _testProjectionState);
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
