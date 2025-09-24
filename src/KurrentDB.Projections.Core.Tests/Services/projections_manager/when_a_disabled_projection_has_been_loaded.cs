// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Management;
using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;
using static KurrentDB.Projections.Core.Messages.ProjectionManagementMessage;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_a_disabled_projection_has_been_loaded<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	protected override void Given() {
		base.Given();
		NoStream("$projections-test-projection-result");
		NoStream("$projections-test-projection-order");
		AllWritesToSucceed("$projections-test-projection-order");
		NoStream("$projections-test-projection-checkpoint");
		ExistingEvent(ProjectionNamesBuilder.ProjectionsRegistrationStream, ProjectionEventTypes.ProjectionCreated, null, "test-projection");
		ExistingEvent(
			"$projections-test-projection", ProjectionEventTypes.ProjectionUpdated, null,
			"""
			{
			  "Query":"fromAll(); on_any(function(){});log('hello-from-projection-definition');",
			  "Mode":"3",
			  "Enabled":false,
			  "HandlerType":"JS",
			  "SourceDefinition":{
			    "AllEvents":true,
			    "AllStreams":true,
			  }
			}
			""");
		AllWritesSucceed();
	}

	private string _projectionName;

	protected override IEnumerable<WhenStep> When() {
		_projectionName = "test-projection";
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
	}

	[Test]
	public void the_projection_source_can_be_retrieved() {
		_manager.Handle(new Command.GetQuery(_bus, _projectionName, RunAs.Anonymous));
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionQuery>().Count());
		var projectionQuery = _consumer.HandledMessages.OfType<ProjectionQuery>().Single();
		Assert.AreEqual(_projectionName, projectionQuery.Name);
	}

	[Test]
	public void the_projection_status_is_stopped() {
		_manager.Handle(new Command.GetStatistics(_bus, null, _projectionName));

		var actual = _consumer.HandledMessages.OfType<Statistics>().ToArray();
		Assert.AreEqual(1, actual.Length);
		Assert.AreEqual(1, actual.Single().Projections.Length);
		Assert.AreEqual(_projectionName, actual.Single().Projections.Single().Name);
		Assert.AreEqual(ManagedProjectionState.Stopped, actual.Single().Projections.Single().LeaderStatus);
	}

	[Test]
	public void the_projection_state_can_be_retrieved() {
		_manager.Handle(new Command.GetState(_bus, _projectionName, ""));
		_queue.Process();

		var actualStats = _consumer.HandledMessages.OfType<Statistics>().ToArray();
		var actualState = _consumer.HandledMessages.OfType<ProjectionState>().ToArray();
		Assert.AreEqual(1, actualStats.Length);
		Assert.AreEqual(_projectionName, actualState.Single().Name);
		Assert.AreEqual("", actualState.Single().State);
	}
}
