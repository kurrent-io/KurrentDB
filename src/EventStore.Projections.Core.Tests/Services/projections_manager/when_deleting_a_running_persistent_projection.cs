// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Messages;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projections_manager;

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
