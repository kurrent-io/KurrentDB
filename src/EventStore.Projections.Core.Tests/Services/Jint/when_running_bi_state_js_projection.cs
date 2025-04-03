// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text.Json;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.Jint;

[TestFixture]
public class when_running_bi_state_js_projection : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
                options({
                    biState: true,
                });
                fromAll().foreachStream().when({
                    type1: function(state, event) {
                        state[0].count = state[0].count + 1;
                        state[1].sharedCount = state[1].sharedCount + 1;
                        return state;
                    }});
            ";
		_state = @"{""count"": 0}";
		_sharedState = @"{""sharedCount"": 0}";
	}

	[Test, Category(_projectionType)]
	public void process_event_counts_events() {
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 10, 5), "stream1", "type1", "category", Guid.NewGuid(), 0, "metadata",
			@"{""a"":""b""}", out var state, out var sharedState, out var emittedEvents);

		Assert.Null(emittedEvents);
		Assert.NotNull(state);
		Assert.NotNull(sharedState);
		var stateJson = JsonDocument.Parse(state);
		var sharedJson = JsonDocument.Parse(sharedState);
		Assert.True(stateJson.RootElement.TryGetProperty("count", out var stateCount));
		Assert.AreEqual(JsonValueKind.Number, stateCount.ValueKind);
		Assert.AreEqual(1, stateCount.GetInt32());

		Assert.True(sharedJson.RootElement.TryGetProperty("sharedCount", out var sharedCount));
		Assert.AreEqual(JsonValueKind.Number, sharedCount.ValueKind);
		Assert.AreEqual(1, sharedCount.GetInt32());
	}
}
