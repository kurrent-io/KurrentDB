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
using KurrentDB.Projections.Core.Services.Management;
using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_a_lifecycle_write_follows_a_metadata_post<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private string _projectionName;
	private readonly byte[] _metadata = Helper.UTF8NoBom.GetBytes("{\"deploy\":\"abc123\"}");

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
				ProjectionManagementMessage.RunAs.System, "JS", @"fromAll().when({$any:function(s,e){return s;}});",
				enabled: true, checkpointsEnabled: true, emitEnabled: true, trackEmittedStreams: true,
				metadata: _metadata);
		yield return
			new ProjectionManagementMessage.Command.Disable(
				_bus, _projectionName, ProjectionManagementMessage.RunAs.System);
	}

	private List<ClientMessage.WriteEvents> DefinitionWrites() {
		var stream = ProjectionNamesBuilder.ProjectionsStreamPrefix + _projectionName;
		return _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>()
			.Where(x => x.EventStreamId == stream && x.Events[0].EventType == ProjectionEventTypes.ProjectionUpdated)
			.ToList();
	}

	[Test, Category("v8")]
	public void the_post_write_carries_the_metadata() {
		CollectionAssert.AreEqual(_metadata, DefinitionWrites().First().Events[0].Metadata);
	}

	[Test, Category("v8")]
	public void the_lifecycle_write_does_not_re_stamp_the_metadata() {
		var writes = DefinitionWrites();
		Assert.Greater(writes.Count, 1, "expected the disable to produce a further definition write");
		Assert.IsEmpty(writes.Last().Events[0].Metadata);
	}
}
