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
public class when_running_a_v8_projection_with_not_passing_filter_by : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
                fromAll().when({$any: 
                    function(state, event) {
                        state.a = event.body.a;
                        return state;
                    }
                })
                .filterBy(function(state) {
                    return state.a == '2';
                });
            ";
	}

	[Test, Category(_projectionType)]
	public void filter_by_that_passes_returns_correct_result() {
		string state;
		EmittedEventEnvelope[] emittedEvents;
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
			"metadata",
			@"{""a"":""2""}", out state, out emittedEvents);
		var result = _stateHandler.TransformStateToResult();

		Assert.IsNotNull(result);
		Assert.AreEqual(@"{""a"":""2""}", result);
	}

	[Test, Category(_projectionType)]
	public void filter_by_that_does_not_pass_returns_correct_result() {
		string state;
		EmittedEventEnvelope[] emittedEvents;
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
			"metadata",
			@"{""a"":""3""}", out state, out emittedEvents);
		var result = _stateHandler.TransformStateToResult();

		Assert.IsNull(result);
	}
}
