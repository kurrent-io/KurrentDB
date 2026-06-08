// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.TestAdapters;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_updating_a_projection_with_metadata<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private string _projectionName;
	private readonly byte[] _updateMetadata = Helper.UTF8NoBom.GetBytes("{\"deploy\":\"def456\"}");

	protected override void Given() {
		_projectionName = "test-projection";
		NoStream("$projections-test-projection");
		NoStream("$projections-test-projection-result");
		NoStream("$projections-test-projection-order");
		AllWritesToSucceed("$projections-test-projection-order");
		NoStream("$projections-test-projection-checkpoint");
		AllWritesSucceed();
	}

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
		yield return
			new ProjectionManagementMessage.Command.Post(
				_bus, ProjectionMode.Continuous, _projectionName,
				ProjectionManagementMessage.RunAs.System, "JS", @"fromAll(); on_any(function(){return {};});",
				enabled: true, checkpointsEnabled: true, emitEnabled: true, trackEmittedStreams: true);
		yield return
			new ProjectionManagementMessage.Command.UpdateQuery(
				_bus, _projectionName, ProjectionManagementMessage.RunAs.System,
				@"fromAll().when({a:function(){return {};}});", emitEnabled: null,
				metadata: _updateMetadata);
	}

	[Test, Category("v8")]
	public void the_update_definition_write_carries_the_metadata() {
		var stream = ProjectionNamesBuilder.ProjectionsStreamPrefix + _projectionName;
		var writes = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>()
			.Where(x => x.EventStreamId == stream && x.Events[0].EventType == ProjectionEventTypes.ProjectionUpdated)
			.ToList();
		CollectionAssert.AreEqual(_updateMetadata, writes.Last().Events[0].Metadata);
	}
}
