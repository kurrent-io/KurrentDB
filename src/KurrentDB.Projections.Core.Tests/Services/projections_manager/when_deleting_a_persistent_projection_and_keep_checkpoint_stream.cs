// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using NUnit.Framework;

using ClientMessageWriteEvents = KurrentDB.Core.Tests.TestAdapters.ClientMessage.WriteEvents;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_deleting_a_persistent_projection_and_keep_checkpoint_stream<TLogFormat, TStreamId> :
		TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private string _projectionName;
	private const string _projectionStateStream = "$projections-test-projection-result";
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
			new ProjectionManagementMessage.Command.Disable(
				_bus, _projectionName, ProjectionManagementMessage.RunAs.System);
		yield return
			new ProjectionManagementMessage.Command.Delete(
				_bus, _projectionName,
				ProjectionManagementMessage.RunAs.System, false, false, false);
	}

	[Test, Category("v8")]
	public void a_projection_deleted_event_is_written() {
		Assert.AreEqual(
			true,
			_consumer.HandledMessages.OfType<ClientMessageWriteEvents>().Any(x =>
				x.Events[0].EventType == ProjectionEventTypes.ProjectionDeleted &&
				Helper.UTF8NoBom.GetString(x.Events[0].Data) == _projectionName));
	}

	[Test, Category("v8")]
	public void should_not_have_attempted_to_delete_the_checkpoint_stream() {
		Assert.IsFalse(
			_consumer.HandledMessages.OfType<ClientMessage.DeleteStream>()
				.Any(x => x.EventStreamId == _projectionCheckpointStream));
	}
}
