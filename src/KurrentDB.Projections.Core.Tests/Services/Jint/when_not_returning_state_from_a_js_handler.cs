// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.Jint;

[TestFixture]
public class when_not_returning_state_from_a_js_handler : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
                fromAll().when({$any: function(state, event) {
                    state.newValue = 'new';
                }});
            ";
	}

	[Test, Category(ProjectionType)]
	public void process_event_should_return_updated_state() {
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category",
			Guid.NewGuid(), 0, "metadata", @"{""a"":""b""}", out var state, out _);
		Assert.IsTrue(state.Contains("\"newValue\":\"new\""));
	}

	[Test, Category(ProjectionType)]
	public void process_event_returns_true() {
		var result = _stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category",
			Guid.NewGuid(), 0, "metadata", @"{""a"":""b""}", out _, out _);

		Assert.IsTrue(result);
	}
}
