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
public class when_updating_a_disabled_projection_query_text<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	protected override void Given() {
		NoStream("$projections-test-projection");
		NoStream("$projections-test-projection-result");
		NoStream("$projections-test-projection-order");
		AllWritesToSucceed("$projections-test-projection-order");
		NoStream("$projections-test-projection-checkpoint");
		AllWritesSucceed();
	}

	private const string ProjectionName = "test-projection";
	private string _newProjectionSource;

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
		yield return
			new Command.Post(
				_bus, ProjectionMode.Continuous, ProjectionName,
				RunAs.System, "JS", "fromAll(); on_any(function(){});log(1);",
				enabled: false, checkpointsEnabled: true, emitEnabled: true, trackEmittedStreams: true);
		// when
		_newProjectionSource = "fromAll(); on_any(function(){});log(2);";
		yield return new Command.UpdateQuery(_bus, ProjectionName, RunAs.System, _newProjectionSource, emitEnabled: null);
	}

	[Test, Category("v8")]
	public void the_projection_source_can_be_retrieved() {
		_manager.Handle(new Command.GetQuery(_bus, ProjectionName, RunAs.Anonymous));

		var projectionQuery = _consumer.HandledMessages.OfType<ProjectionQuery>().Single();
		Assert.AreEqual(ProjectionName, projectionQuery.Name);
		Assert.AreEqual(_newProjectionSource, projectionQuery.Query);
	}

	[Test, Category("v8")]
	public void the_projection_status_is_still_stopped() {
		_manager.Handle(new Command.GetStatistics(_bus, null, ProjectionName));

		var actual = _consumer.HandledMessages.OfType<Statistics>().ToArray();
		Assert.AreEqual(1, actual.Length);
		Assert.AreEqual(1, actual.Single().Projections.Length);
		Assert.AreEqual(ProjectionName, actual.Single().Projections.Single().Name);
		Assert.AreEqual(ManagedProjectionState.Stopped, actual.Single().Projections.Single().LeaderStatus);
	}

	[Test, Category("v8")]
	public void the_projection_state_can_be_retrieved() {
		_manager.Handle(new Command.GetState(_bus, ProjectionName, ""));
		_queue.Process();

		var actual = _consumer.HandledMessages.OfType<ProjectionState>().ToArray();
		Assert.AreEqual(1, actual.Length);
		Assert.AreEqual(ProjectionName, actual.Single().Name);
		Assert.AreEqual("", actual.Single().State);
	}
}
