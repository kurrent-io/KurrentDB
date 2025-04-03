// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.Jint;

[TestFixture]
public class when_running_a_projection_with_created_handler : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
                fromAll().foreachStream().when({
                    $created: function(s, e) {
                        emit('stream1', 'event1', {a:1});
                    }   
                });
            ";
		_state = @"{}";
	}

	[Test, Category(_projectionType)]
	public void returns_emitted_events() {
		var e = new ResolvedEvent(
			"stream", 0, "stream", 0, false, new TFPos(1000, 900), Guid.NewGuid(), "event", true, "{}",
			"{\"m\":1}");

		EmittedEventEnvelope[] emittedEvents;
		_stateHandler.ProcessPartitionCreated(
			"partition", CheckpointTag.FromPosition(0, 10, 5), e, out emittedEvents);

		Assert.IsNotNull(emittedEvents);
		Assert.AreEqual(1, emittedEvents.Length);
		Assert.AreEqual("stream1", emittedEvents[0].Event.StreamId);
		Assert.AreEqual("event1", emittedEvents[0].Event.EventType);
		Assert.AreEqual("{\"a\":1}", emittedEvents[0].Event.Data);
	}
}
