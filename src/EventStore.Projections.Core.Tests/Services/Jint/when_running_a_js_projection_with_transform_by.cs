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
public class when_running_a_js_projection_with_transform_by : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
                fromAll().when({
                    $any: function(state, event) {
                        state.a = '1';
                        return state;
                    }
                })
                .transformBy(function(state) {
                    state.b = '2';
                    return state;
                });
            ";
	}

	[Test, Category(_projectionType)]
	public void transform_state_returns_correct_result() {
		string state;
		EmittedEventEnvelope[] emittedEvents;
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
			"metadata",
			@"{}", out state, out emittedEvents);
		var result = _stateHandler.TransformStateToResult();

		Assert.IsNotNull(result);
		Assert.AreEqual(@"{""a"":""1"",""b"":""2""}", result);
	}
}
