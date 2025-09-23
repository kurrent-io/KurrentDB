// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Data;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Services;
using NUnit.Framework;
using static KurrentDB.Projections.Core.Messages.ProjectionManagementMessage;

namespace KurrentDB.Projections.Core.Tests.Services.projections_system;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
class when_requesting_state_from_a_faulted_projection<TLogFormat, TStreamId> : with_projection_config<TLogFormat, TStreamId> {
	private TFPos _message1Position;

	protected override void Given() {
		base.Given();
		NoOtherStreams();
		_message1Position = ExistingEvent("stream1", "message1", null, "{}");
		_projectionSource = "fromAll().when({message1: function(s,e){ throw 1; }});";
	}

	protected override IEnumerable<WhenStep> When() {
		yield return new Command.Post(
			Envelope, ProjectionMode.Continuous, _projectionName, RunAs.System,
			"js", _projectionSource, enabled: true, checkpointsEnabled: true, emitEnabled: true,
			trackEmittedStreams: true);
		yield return Yield;
		yield return new Command.GetState(Envelope, _projectionName, "");
	}

	protected override bool GivenStartSystemProjections() => true;

	[Test]
	public void reported_state_is_before_the_fault_position() {
		var states = HandledMessages.OfType<ProjectionState>().ToArray();
		Assert.AreEqual(1, states.Length);
		var state = states[0];
		Assert.That(state.Position.Streams.Count == 1);
		Assert.That(state.Position.Streams.Keys.First() == "message1");
		Assert.That(state.Position.Streams["message1"] == -1);
		Assert.That(state.Position.Position <= _message1Position, "{0} <= {1}", state.Position.Position, _message1Position);
	}
}
