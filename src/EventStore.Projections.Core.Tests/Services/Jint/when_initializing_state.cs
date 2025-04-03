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
public class when_initializing_state : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
                fromAll().when({
                    $init: function() {
                        return { test: '1' };
                    },
                    
                    type1: function(state, event) {
                        return state;
                    },
                }).transformBy(function(s) { return s;});
            ";
	}

	[Test, Category(_projectionType)]
	public void process_event_should_return_initialized_state() {
		string state;
		EmittedEventEnvelope[] emittedEvents;
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category",
			Guid.NewGuid(), 0, "metadata", @"{""a"":""b""}", out state, out emittedEvents);
		Assert.IsTrue(state.Contains("\"test\":\"1\""));
	}

	[Test, Category(_projectionType)]
	public void transform_state_should_return_initialized_state() {
		var result = _stateHandler.TransformStateToResult();
		Assert.IsTrue(result.Contains("\"test\":\"1\""));
	}
}
