// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Messaging;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Management;
using NUnit.Framework;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services;

namespace EventStore.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_a_disabled_projection_has_been_loaded<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	protected override void Given() {
		base.Given();
		NoStream("$projections-test-projection-result");
		NoStream("$projections-test-projection-order");
		AllWritesToSucceed("$projections-test-projection-order");
		NoStream("$projections-test-projection-checkpoint");
		ExistingEvent(ProjectionNamesBuilder.ProjectionsRegistrationStream, ProjectionEventTypes.ProjectionCreated,
			null, "test-projection");
		ExistingEvent(
			"$projections-test-projection", ProjectionEventTypes.ProjectionUpdated, null,
			@"{
                    ""Query"":""fromAll(); on_any(function(){});log('hello-from-projection-definition');"", 
                    ""Mode"":""3"", 
                    ""Enabled"":false, 
                    ""HandlerType"":""JS"",
                    ""SourceDefinition"":{
                        ""AllEvents"":true,
                        ""AllStreams"":true,
                    }
                }");
		AllWritesSucceed();
	}

	private string _projectionName;

	protected override IEnumerable<WhenStep> When() {
		_projectionName = "test-projection";
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
	}

	[Test]
	public void the_projection_source_can_be_retrieved() {
		_manager.Handle(
			new ProjectionManagementMessage.Command.GetQuery(
				_bus, _projectionName, ProjectionManagementMessage.RunAs.Anonymous));
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionQuery>().Count());
		var projectionQuery =
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionQuery>().Single();
		Assert.AreEqual(_projectionName, projectionQuery.Name);
	}

	[Test]
	public void the_projection_status_is_stopped() {
		_manager.Handle(
			new ProjectionManagementMessage.Command.GetStatistics(_bus, null, _projectionName,
				false));

		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().Count());
		Assert.AreEqual(
			1,
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().Single().Projections.Length);
		Assert.AreEqual(
			_projectionName,
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().Single().Projections.Single()
				.Name);
		Assert.AreEqual(
			ManagedProjectionState.Stopped,
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().Single().Projections.Single()
				.LeaderStatus);
	}

	[Test]
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
