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
public class when_posting_a_projection_with_metadata<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
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
	}

	private List<ClientMessage.WriteEvents> DefinitionWrites() {
		var stream = ProjectionNamesBuilder.ProjectionsStreamPrefix + _projectionName;
		return _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>()
			.Where(x => x.EventStreamId == stream && x.Events[0].EventType == ProjectionEventTypes.ProjectionUpdated)
			.ToList();
	}

	[Test, Category("v8")]
	public void the_definition_event_carries_the_metadata() {
		var writes = DefinitionWrites();
		CollectionAssert.IsNotEmpty(writes);
		CollectionAssert.AreEqual(_metadata, writes.First().Events[0].Metadata);
	}

	[Test, Category("v8")]
	public void the_definition_event_metadata_is_property_metadata() {
		Assert.IsTrue(DefinitionWrites().First().Events[0].IsPropertyMetadata);
	}

	[Test, Category("v8")]
	public void no_later_write_re_stamps_the_metadata() {
		Assert.IsTrue(DefinitionWrites().Skip(1).All(w => w.Events[0].Metadata.Length == 0));
	}

	[Test, Category("v8")]
	public void the_definition_event_body_is_the_unchanged_persisted_state() {
		var state = DefinitionWrites().First().Events[0].Data.ParseJson<ManagedProjection.PersistedState>();
		Assert.AreEqual("JS", state.HandlerType);
		Assert.AreEqual(ProjectionMode.Continuous, state.Mode);
	}
}
