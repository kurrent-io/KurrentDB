// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.Jint;

[TestFixture]
public class when_running_body_reflecting_v8_projection : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
                fromAll().when({$any: 
                    function(state, event) {
                        if (event.body) 
                            return event.body; 
                            else return {};
                    }
                });
            ";
	}

	[Test, Category(_projectionType)]
	public void process_event_should_reflect_event() {
		string state;
		EmittedEventEnvelope[] emittedEvents;
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
			"metadata",
			@"{""a"":""b""}", out state, out emittedEvents);
		Assert.AreEqual(@"{""a"":""b""}", state);
	}

	[Test, Category(_projectionType)]
	public void process_event_should_not_reflect_non_json_events_even_if_valid_json() {
		string state;
		EmittedEventEnvelope[] emittedEvents;
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
			"metadata",
			@"{""a"":""b""}", out state, out emittedEvents, isJson: false);
		Assert.AreEqual(@"{}", state);
	}
}
