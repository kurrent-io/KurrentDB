// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Globalization;
using System.Text.Json;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.Jint;

[TestFixture]
public class when_running_counting_js_projection : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
                fromAll().when({$any:
                    function(state, event) {
                        state.count = state.count + 1;
                        return state;
                    }});
            ";
		_state = @"{""count"": 0}";
	}

	[Test, Category(ProjectionType)]
	public void process_event_counts_events() {
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 10, 5), "stream1", "type1", "category", Guid.NewGuid(), 0, "metadata",
			@"{""a"":""b""}", out var state, out var emittedEvents);
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 20, 15), "stream1", "type1", "category", Guid.NewGuid(), 1,
			"metadata", @"{""a"":""b""}", out state, out emittedEvents);

		Assert.Null(emittedEvents);
		Assert.NotNull(state);

		var stateJson = JsonDocument.Parse(state);
		Assert.True(stateJson.RootElement.TryGetProperty("count", out var stateCount));
		Assert.AreEqual(JsonValueKind.Number, stateCount.ValueKind);
		Assert.AreEqual(2, stateCount.GetInt32());

	}

	[Test, Category(ProjectionType), Category("Manual"), Explicit]
	public void can_handle_million_events() {
		for (var i = 0; i < 1000000; i++) {
			_logged.Clear();
			_stateHandler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, i * 10, i * 10 - 5), "stream" + i, "type" + i, "category",
				Guid.NewGuid(), 0,
				"metadata", $$"""{"a":"{{i}}"}""", out _, out _);
			Assert.AreEqual(1, _logged.Count);
			Assert.AreEqual((i + 1).ToString(CultureInfo.InvariantCulture), _logged[_logged.Count - 1]);
		}
	}
}
