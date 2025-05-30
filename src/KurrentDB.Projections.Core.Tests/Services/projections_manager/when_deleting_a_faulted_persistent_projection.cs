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
public class when_deleting_a_faulted_persistent_projection<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private string _projectionName;
	private const string _projectionCheckpointStream = "$projections-test-projection-checkpoint";
	private const string _projectionEmittedStreamsStream = "$projections-test-projection-emittedstreams";

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
				ProjectionManagementMessage.RunAs.System, "JS", @"faulted_projection",
				enabled: true, checkpointsEnabled: true, emitEnabled: true, trackEmittedStreams: true);
		yield return
			new ProjectionManagementMessage.Command.Delete(
				_bus, _projectionName,
				ProjectionManagementMessage.RunAs.System, true, true, true);
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
	public void should_have_attempted_to_delete_the_checkpoint_stream() {
		Assert.IsTrue(
			_consumer.HandledMessages.OfType<ClientMessage.DeleteStream>()
				.Any(x => x.EventStreamId == _projectionCheckpointStream));
	}

	[Test, Category("v8")]
	public void should_have_attempted_to_delete_the_emitted_streams_stream() {
		Assert.IsTrue(
			_consumer.HandledMessages.OfType<ClientMessage.DeleteStream>()
				.Any(x => x.EventStreamId == _projectionEmittedStreamsStream));
	}
}
