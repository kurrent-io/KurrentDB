// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Messaging;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Management;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projections_manager;

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

	private string _projectionName;
	private string _newProjectionSource;

	protected override IEnumerable<WhenStep> When() {
		_projectionName = "test-projection";
		yield return (new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));
		yield return
			(new ProjectionManagementMessage.Command.Post(
				_bus, ProjectionMode.Continuous, _projectionName,
				ProjectionManagementMessage.RunAs.System, "JS", @"fromAll(); on_any(function(){});log(1);",
				enabled: false, checkpointsEnabled: true, emitEnabled: true, trackEmittedStreams: true));
		// when
		_newProjectionSource = @"fromAll(); on_any(function(){});log(2);";
		yield return
			(new ProjectionManagementMessage.Command.UpdateQuery(
				_bus, _projectionName, ProjectionManagementMessage.RunAs.System,
				_newProjectionSource, emitEnabled: null));
	}

	[Test, Category("v8")]
	public void the_projection_source_can_be_retrieved() {
		_manager.Handle(
			new ProjectionManagementMessage.Command.GetQuery(
				_bus, _projectionName, ProjectionManagementMessage.RunAs.Anonymous));
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionQuery>().Count());
		var projectionQuery =
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionQuery>().Single();
		Assert.AreEqual(_projectionName, projectionQuery.Name);
		Assert.AreEqual(_newProjectionSource, projectionQuery.Query);
	}

	[Test, Category("v8")]
	public void the_projection_status_is_still_stopped() {
		_manager.Handle(
			new ProjectionManagementMessage.Command.GetStatistics(_bus, null, _projectionName,
				false));

		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().Count());
		Assert.AreEqual(
			1,
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().Single().Projections.Length);
		Assert.AreEqual(
			_projectionName,
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
				.Single()
				.Projections.Single()
				.Name);
		Assert.AreEqual(
			ManagedProjectionState.Stopped,
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
				.Single()
				.Projections.Single()
				.LeaderStatus);
	}

	[Test, Category("v8")]
	public void the_projection_state_can_be_retrieved() {
		_manager.Handle(
			new ProjectionManagementMessage.Command.GetState(_bus, _projectionName, ""));
		_queue.Process();

		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionState>().Count());
		Assert.AreEqual(
			_projectionName,
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionState>().Single().Name);
		Assert.AreEqual(
			"", _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionState>().Single().State);
	}
}
