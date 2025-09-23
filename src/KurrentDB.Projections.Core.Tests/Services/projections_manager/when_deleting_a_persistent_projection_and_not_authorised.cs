// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.TestAdapters;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using NUnit.Framework;
using static KurrentDB.Projections.Core.Messages.ProjectionManagementMessage;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_deleting_a_persistent_projection_and_not_authorised<TLogFormat, TStreamId>
	: TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private const string ProjectionName = "test-projection";

	protected override void Given() {
		AllWritesSucceed();
		NoOtherStreams();
	}

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
		yield return new Command.Post(_bus, ProjectionMode.Continuous, ProjectionName,
			RunAs.System, "JS", "fromAll().when({$any:function(s,e){return s;}});",
			enabled: true, checkpointsEnabled: true, emitEnabled: false, trackEmittedStreams: true);
		yield return new Command.Disable( _bus, ProjectionName, RunAs.System);
		yield return new Command.Delete( _bus, ProjectionName, RunAs.Anonymous, false, false, false);
	}

	[Test, Category("v8")]
	public void a_projection_deleted_event_is_not_written() {
		Assert.AreNotEqual(
			ProjectionEventTypes.ProjectionDeleted,
			_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Last().Events[0].EventType,
			$"{ProjectionEventTypes.ProjectionDeleted} event was not supposed to be written");
	}
}
