// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.Jint;

[TestFixture]
public class when_js_projection_loading_state : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
                fromAll().when({$any:
                    function(state, event) {
                        return state;
                    }
                });
            ";
		_state = @"{""A"":""A"",""B"":""B""}";
	}

	[Test, Category(ProjectionType)]
	public void the_state_is_loaded() {
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
			"metadata", """{"x":"y"}""", out var state, out _);

		Assert.AreEqual(_state, state);
	}
}
