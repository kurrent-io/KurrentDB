// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Messaging;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_updating_a_persistent_projection_emit_enabled_option<TLogFormat, TStreamId> :
	TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	protected override void Given() {
		NoStream("$projections-test-projection");
		NoStream("$projections-test-projection-result");
		NoStream("$projections-test-projection-order");
		AllWritesToSucceed("$projections-test-projection-order");
		NoStream("$projections-test-projection-checkpoint");
		AllWritesSucceed();
	}

	private string _projectionName;
	private string _source;

	protected override IEnumerable<WhenStep> When() {
		_projectionName = "test-projection";
		_source = @"fromAll(); on_any(function(){});log(1);";
		yield return (new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));
		yield return
			(new ProjectionManagementMessage.Command.Post(
				_bus, ProjectionMode.Continuous, _projectionName,
				ProjectionManagementMessage.RunAs.System, "JS", _source, enabled: true, checkpointsEnabled: true,
				emitEnabled: true, trackEmittedStreams: true));
		// when
		yield return
			(new ProjectionManagementMessage.Command.UpdateQuery(
				_bus, _projectionName, ProjectionManagementMessage.RunAs.System,
				_source, emitEnabled: false));
	}

	[Test, Category("v8")]
	public void emit_enabled_options_remains_unchanged() {
		_manager.Handle(
			new ProjectionManagementMessage.Command.GetQuery(
				_bus, _projectionName, ProjectionManagementMessage.RunAs.Anonymous));
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionQuery>().Count());
		var projectionQuery =
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionQuery>().Single();
		Assert.AreEqual(_projectionName, projectionQuery.Name);
		Assert.AreEqual(false, projectionQuery.EmitEnabled);
	}
}
