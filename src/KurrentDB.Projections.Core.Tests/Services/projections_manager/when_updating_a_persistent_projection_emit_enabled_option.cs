// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using NUnit.Framework;
using static KurrentDB.Projections.Core.Messages.ProjectionManagementMessage;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_updating_a_persistent_projection_emit_enabled_option<TLogFormat, TStreamId>
	: TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	protected override void Given() {
		NoStream("$projections-test-projection");
		NoStream("$projections-test-projection-result");
		NoStream("$projections-test-projection-order");
		AllWritesToSucceed("$projections-test-projection-order");
		NoStream("$projections-test-projection-checkpoint");
		AllWritesSucceed();
	}

	private const string ProjectionName = "test-projection";
	private const string Source = "fromAll();_source; on_any(function(){});log(1);";

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
		yield return new Command.Post(
			_bus, ProjectionMode.Continuous, ProjectionName,
			RunAs.System, "JS", Source, enabled: true, checkpointsEnabled: true,
			emitEnabled: true, trackEmittedStreams: true);
		// when
		yield return new Command.UpdateQuery(_bus, ProjectionName, RunAs.System, Source, emitEnabled: false);
	}

	[Test, Category("v8")]
	public void emit_enabled_options_remains_unchanged() {
		_manager.Handle(new Command.GetQuery(_bus, ProjectionName, RunAs.Anonymous));

		var projectionQuery = _consumer.HandledMessages.OfType<ProjectionQuery>().Single();
		Assert.AreEqual(ProjectionName, projectionQuery.Name);
		Assert.AreEqual(false, projectionQuery.EmitEnabled);
	}
}
