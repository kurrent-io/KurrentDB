// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Management;
using NUnit.Framework;
using static KurrentDB.Projections.Core.Messages.ProjectionManagementMessage;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_updating_an_onetime_projection_query_text<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private const string ProjectionName = "test-projection";
	private const string NewProjectionSource = @"fromAll(); on_any(function(){});log(2);";

	protected override void Given() {
		NoOtherStreams();
	}

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
		yield return new Command.Post(
			_bus, ProjectionMode.Transient, ProjectionName,
			RunAs.Anonymous, "JS", @"fromAll(); on_any(function(){});log(1);",
			enabled: true, checkpointsEnabled: false, emitEnabled: false, trackEmittedStreams: true);
		// when
		yield return new Command.UpdateQuery( _bus, ProjectionName, RunAs.Anonymous, NewProjectionSource, emitEnabled: null);
	}

	[Test, Category("v8")]
	public void the_projection_source_can_be_retrieved() {
		_manager.Handle(new Command.GetQuery(_bus, ProjectionName, RunAs.Anonymous));

		var projectionQuery = _consumer.HandledMessages.OfType<ProjectionQuery>().Single();
		Assert.AreEqual(ProjectionName, projectionQuery.Name);
		Assert.AreEqual(NewProjectionSource, projectionQuery.Query);
	}

	[Test, Category("v8")]
	public void the_projection_status_is_still_running() {
		_manager.Handle(new Command.GetStatistics(_bus, null, ProjectionName));

		var stats = _consumer.HandledMessages.OfType<Statistics>().Single();
		Assert.AreEqual(1, stats.Projections.Length);
		Assert.AreEqual(ProjectionName, stats.Projections.Single().Name);
		Assert.AreEqual(ManagedProjectionState.Running, stats.Projections.Single().LeaderStatus);
	}

	[Test, Category("v8")]
	public void the_projection_state_can_be_retrieved() {
		_manager.Handle(new Command.GetState(_bus, ProjectionName, ""));
		_queue.Process();

		var state = _consumer.HandledMessages.OfType<ProjectionState>().Single();
		Assert.AreEqual(ProjectionName, state.Name);
		Assert.AreEqual("", state.State);
	}
}
