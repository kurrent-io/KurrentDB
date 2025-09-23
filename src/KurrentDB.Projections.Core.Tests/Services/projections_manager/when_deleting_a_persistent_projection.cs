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
using static KurrentDB.Projections.Core.Messages.ProjectionManagementMessage;
using ClientMessageWriteEvents = KurrentDB.Core.Tests.TestAdapters.ClientMessage.WriteEvents;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_deleting_a_persistent_projection<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private string _projectionName;
	private const string ProjectionCheckpointStream = "$projections-test-projection-checkpoint";
	private const string ProjectionEmittedStreamsStream = "$projections-test-projection-emittedstreams";

	protected override void Given() {
		_projectionName = "test-projection";
		AllWritesSucceed();
		NoOtherStreams();
	}

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
		yield return
			new Command.Post(
				_bus, ProjectionMode.Continuous, _projectionName,
				RunAs.System, "JS", "fromAll().when({$any:function(s,e){return s;}});",
				enabled: true, checkpointsEnabled: true, emitEnabled: true, trackEmittedStreams: true);
		yield return new Command.Disable(_bus, _projectionName, RunAs.System);
		yield return new Command.Delete(_bus, _projectionName, RunAs.System, true, true, true);
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
				.Any(x => x.EventStreamId == ProjectionCheckpointStream));
	}

	[Test, Category("v8")]
	public void should_have_attempted_to_delete_the_emitted_streams_stream() {
		Assert.IsTrue(
			_consumer.HandledMessages.OfType<ClientMessage.DeleteStream>()
				.Any(x => x.EventStreamId == ProjectionEmittedStreamsStream));
	}
}
