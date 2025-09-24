// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.Jint;

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

	[Test, Category(ProjectionType)]
	public void process_event_should_return_initialized_state() {
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category",
			Guid.NewGuid(), 0, "metadata", @"{""a"":""b""}", out var state, out _);
		Assert.IsTrue(state.Contains("\"test\":\"1\""));
	}

	[Test, Category(ProjectionType)]
	public void transform_state_should_return_initialized_state() {
		var result = _stateHandler.TransformStateToResult();
		Assert.IsTrue(result.Contains("\"test\":\"1\""));
	}
}
