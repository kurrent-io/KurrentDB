// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Messages;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Management;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_posting_a_persistent_projection<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private string _projectionName;

	protected override void Given() {
		_projectionName = "test-projection";
		AllWritesQueueUp();
		NoOtherStreams();
	}

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
		yield return
			new ProjectionManagementMessage.Command.Post(
				_bus, ProjectionMode.Continuous, _projectionName,
				ProjectionManagementMessage.RunAs.System, "JS", @"fromAll().when({$any:function(s,e){return s;}});",
				enabled: true, checkpointsEnabled: true, emitEnabled: true, trackEmittedStreams: true);
		OneWriteCompletes();
	}

	[Test, Category("v8")]
	public void the_projection_status_is_writing() {
		_manager.Handle(
			new ProjectionManagementMessage.Command.GetStatistics(_bus, null, _projectionName,
				true));
		Assert.AreEqual(
			ManagedProjectionState.Prepared,
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().Single().Projections[0]
				.LeaderStatus);
	}

	[Test, Category("v8")]
	public void a_projection_created_event_is_written() {
		var createdEventWrite = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().FirstOrDefault();
		Assert.NotNull(createdEventWrite);
		Assert.AreEqual(ProjectionNamesBuilder.ProjectionsRegistrationStream, createdEventWrite.EventStreamId);
		Assert.AreEqual(ProjectionEventTypes.ProjectionCreated, createdEventWrite.Events[0].EventType);
		Assert.AreEqual(_projectionName, Helper.UTF8NoBom.GetString(createdEventWrite.Events[0].Data));
	}

	[Test, Category("v8")]
	public void persisted_projection_state_is_written_with_empty_execution_timeout() {
		var persistedStateStream = ProjectionNamesBuilder.ProjectionsStreamPrefix + _projectionName;
		var persistedStateWrite = _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>()
			.FirstOrDefault(x => x.EventStreamId == persistedStateStream);

		Assert.NotNull(persistedStateWrite);
		Assert.AreEqual(ProjectionEventTypes.ProjectionUpdated, persistedStateWrite.Events[0].EventType);
		var actualState = persistedStateWrite.Events[0].Data.ParseJson<ManagedProjection.PersistedState>();
		Assert.IsNull(actualState.ProjectionExecutionTimeout);
	}

	[Test, Category("v8")]
	public void a_projection_updated_message_is_not_published() {
		// not published until all writes complete
		Assert.AreEqual(0, _consumer.HandledMessages.OfType<ProjectionManagementMessage.Updated>().Count());
	}
}
