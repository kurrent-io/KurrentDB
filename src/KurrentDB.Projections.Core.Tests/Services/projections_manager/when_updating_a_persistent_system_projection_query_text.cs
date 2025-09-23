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
public class when_updating_a_continous_system_projection_query_text<TLogFormat, TStreamId>
	: TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	protected override void Given() {
		NoStream("$projections-$by_correlation_id");
		NoStream("$projections-$by_correlation_id-result");
		NoStream("$projections-$by_correlation_id-order");
		AllWritesToSucceed("$projections-$by_correlation_id-order");
		NoStream("$projections-$by_correlation_id-checkpoint");
		AllWritesSucceed();
	}

	private const string ProjectionName = "$by_correlation_id";
	private const string NewProjectionSource = "{\"correlationIdProperty\":\"$updateCorrelationId\"}";

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
		yield return new Command.Post(
			_bus, ProjectionMode.Continuous, ProjectionName,
			RunAs.System, "native:KurrentDB.Projections.Core.Standard.ByCorrelationId",
			"{\"correlationIdProperty\":\"$myCorrelationId\"}",
			enabled: true, checkpointsEnabled: true, emitEnabled: true, trackEmittedStreams: true);
		// when
		yield return new Command.UpdateQuery(_bus, ProjectionName, RunAs.System, NewProjectionSource, emitEnabled: null);
	}

	[Test, Category("v8")]
	public void the_projection_source_can_be_retrieved() {
		_manager.Handle(new Command.GetQuery(_bus, ProjectionName, RunAs.Anonymous));

		var projectionQuery = _consumer.HandledMessages.OfType<ProjectionQuery>().Single();
		Assert.AreEqual(ProjectionName, projectionQuery.Name);
		Assert.AreEqual(NewProjectionSource, projectionQuery.Query);
	}

	[Test, Category("v8")]
	public void emit_enabled_options_remains_unchanged() {
		_manager.Handle(new Command.GetQuery(_bus, ProjectionName, RunAs.Anonymous));

		var projectionQuery = _consumer.HandledMessages.OfType<ProjectionQuery>().Single();
		Assert.AreEqual(ProjectionName, projectionQuery.Name);
		Assert.AreEqual(true, projectionQuery.EmitEnabled);
	}

	[Test, Category("v8")]
	public void the_projection_status_is_still_running() {
		_manager.Handle(new Command.GetStatistics(_bus, null, ProjectionName));

		var actual = _consumer.HandledMessages.OfType<Statistics>().Single();
		Assert.AreEqual(1, actual.Projections.Length);
		Assert.AreEqual(ProjectionName, actual.Projections.Single().Name);
		Assert.AreEqual(ManagedProjectionState.Running, actual.Projections.Single().LeaderStatus);
	}

	[Test, Category("v8")]
	public void the_projection_state_can_be_retrieved() {
		_manager.Handle(new Command.GetState(_bus, ProjectionName, ""));
		_queue.Process();

		var state = _consumer.HandledMessages.OfType<ProjectionState>().Single();
		Assert.AreEqual(ProjectionName, state.Name);
		// empty? why?
		Assert.AreEqual("", state.State);
	}

	[Test, Category("v8")]
	public void correct_query_has_been_prepared() {
		var lastPrepared = _consumer.HandledMessages.OfType<CoreProjectionStatusMessage.Prepared>().LastOrDefault();
		Assert.IsNotNull(lastPrepared);
		Assert.IsTrue(lastPrepared.SourceDefinition.AllEvents);
	}
}
