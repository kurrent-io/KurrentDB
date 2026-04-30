// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.Jint;

[TestFixture]
public class when_running_bi_state_js_projection_with_string_state : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
                options({
                    biState: true,
                });
                fromAll().foreachStream().when({
                    type1: function(state, event) {
                        return [""hello"", state[1]];
                    }});
            ";
		_state = @"{}";
		_sharedState = @"{""sharedCount"": 0}";
	}

	[Test, Category(_projectionType)]
	public void state_returned_as_raw_string_not_json_encoded() {
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 10, 5), "stream1", "type1", "category", Guid.NewGuid(), 0, "metadata",
			@"{""a"":""b""}", out var state, out var sharedState, out var emittedEvents);

		Assert.AreEqual("hello", state);
	}
}
