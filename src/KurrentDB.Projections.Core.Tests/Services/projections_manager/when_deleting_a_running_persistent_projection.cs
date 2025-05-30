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

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_deleting_a_running_persistent_projection<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private string _projectionName;
	private const string _projectionCheckpointStream = "$projections-test-projection-checkpoint";

	protected override void Given() {
		_projectionName = "test-projection";
		AllWritesSucceed();
		NoOtherStreams();
	}

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
		yield return
			new ProjectionManagementMessage.Command.Post(
				_bus, ProjectionMode.Continuous, _projectionName,
				ProjectionManagementMessage.RunAs.System, "JS", @"fromAll().when({$any:function(s,e){return s;}});",
				enabled: true, checkpointsEnabled: true, emitEnabled: true, trackEmittedStreams: true);
		yield return
			new ProjectionManagementMessage.Command.Delete(
				_bus, _projectionName,
				ProjectionManagementMessage.RunAs.System, true, true, false);
	}

	[Test, Category("v8")]
	public void a_projection_deleted_event_is_not_written() {
		var projectionDeletedEventExists = _consumer.HandledMessages.Any(x =>
			x.GetType() == typeof(ClientMessage.WriteEvents) &&
			((ClientMessage.WriteEvents)x).Events[0].EventType == ProjectionEventTypes.ProjectionDeleted);
		Assert.IsFalse(projectionDeletedEventExists,
			$"Expected that the {ProjectionEventTypes.ProjectionDeleted} event not to have been written");
	}
}
