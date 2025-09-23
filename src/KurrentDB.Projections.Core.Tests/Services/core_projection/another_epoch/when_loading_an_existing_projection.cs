// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.another_epoch;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_starting_an_existing_projection<TLogFormat, TStreamId> : TestFixtureWithCoreProjectionStarted<TLogFormat, TStreamId> {
	private const string TestProjectionState = """{"test":1}""";

	protected override void Given() {
		_version = new ProjectionVersion(1, 2, 2);
		ExistingEvent(
			"$projections-projection-result", "Result",
			"""{"v":1, "c": 100, "p": 50}""", TestProjectionState);
		ExistingEvent(
			"$projections-projection-checkpoint", ProjectionEventTypes.ProjectionCheckpoint,
			"""{"v":1, "c": 100, "p": 50}""", TestProjectionState);
		ExistingEvent(
			"$projections-projection-result", "Result",
			"""{"v":1, "c": 200, "p": 150}""", TestProjectionState);
		ExistingEvent(
			"$projections-projection-result", "Result",
			"""{"v":1, "c": 300, "p": 250}""", TestProjectionState);
	}

	protected override void When() {
	}

	[Test]
	public void should_subscribe_from_the_beginning() {
		Assert.AreEqual(1, _subscribeProjectionHandler.HandledMessages.Count);
		Assert.AreEqual(0, _subscribeProjectionHandler.HandledMessages[0].FromPosition.Position.CommitPosition);
		Assert.AreEqual(-1, _subscribeProjectionHandler.HandledMessages[0].FromPosition.Position.PreparePosition);
	}
}
