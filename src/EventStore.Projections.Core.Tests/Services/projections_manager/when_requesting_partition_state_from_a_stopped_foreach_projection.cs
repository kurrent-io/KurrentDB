// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Messaging;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services;

namespace EventStore.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_requesting_partition_state_from_a_stopped_foreach_projection<TLogFormat, TStreamId> :
	TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	protected override void Given() {
		NoStream("$projections-test-projection-order");
		ExistingEvent(ProjectionNamesBuilder.ProjectionsRegistrationStream, ProjectionEventTypes.ProjectionCreated,
			null, "test-projection");
		ExistingEvent(
			"$projections-test-projection", ProjectionEventTypes.ProjectionUpdated, null,
			@"{""Query"":""fromCategory('test').foreachStream().when({'e': function(s,e){}})"", 
                    ""Mode"":""3"", ""Enabled"":false, ""HandlerType"":""JS"",
                    ""SourceDefinition"":{
                        ""AllEvents"":true,
                        ""AllStreams"":false,
                        ""Streams"":[""$ce-test""]
                    }
                }");
		ExistingEvent("$projections-test-projection-a-checkpoint", ProjectionEventTypes.PartitionCheckpoint,
			@"{""s"":{""$ce-test"": 9}}", @"{""data"":1}");
		NoStream("$projections-test-projection-b-checkpoint");
		ExistingEvent("$projections-test-projection-checkpoint", ProjectionEventTypes.ProjectionCheckpoint,
			@"{""s"":{""$ce-test"": 10}}", @"{}");
		AllWritesSucceed();
	}

	private string _projectionName;

	protected override IEnumerable<WhenStep> When() {
		_projectionName = "test-projection";
		// when
		yield return (new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));
	}

	[Test]
	public void the_projection_state_can_be_retrieved() {
		_manager.Handle(
			new ProjectionManagementMessage.Command.GetState(_bus, _projectionName, "a"));
		_queue.Process();

		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionState>().Count());

		var first = _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionState>().First();
		Assert.AreEqual(_projectionName, first.Name);
		Assert.AreEqual(@"{""data"":1}", first.State);

		_manager.Handle(
			new ProjectionManagementMessage.Command.GetState(_bus, _projectionName, "b"));
		_queue.Process();

		Assert.AreEqual(2, _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionState>().Count());
		var second = _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionState>().Skip(1)
			.First();
		Assert.AreEqual(_projectionName, second.Name);
		Assert.AreEqual("", second.State);
	}
}
