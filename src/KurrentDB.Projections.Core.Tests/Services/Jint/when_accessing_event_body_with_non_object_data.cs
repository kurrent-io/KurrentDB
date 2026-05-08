// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text.Json;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.Jint;

[TestFixture]
public class when_accessing_event_body_with_non_object_data : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
            fromAll().when({$any:
                function(state, event) {
                    state.body = event.body;
                    return state;
                }
            });
        ";
		_state = @"{}";
	}

	private string ProcessWithRaw(string raw) {
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 10, 5), "stream1", "type1", "category", Guid.NewGuid(), 0, "{}",
			raw, out var state, out _, out _);
		return state;
	}

	[Test, Category(_projectionType)]
	public void raw_null_body_does_not_throw_and_yields_null() {
		var state = ProcessWithRaw("null");
		var doc = JsonDocument.Parse(state);
		Assert.True(doc.RootElement.TryGetProperty("body", out var body));
		Assert.AreEqual(JsonValueKind.Null, body.ValueKind);
	}

	[Test, Category(_projectionType)]
	public void raw_number_body_does_not_throw_and_yields_number() {
		var state = ProcessWithRaw("42");
		var doc = JsonDocument.Parse(state);
		Assert.True(doc.RootElement.TryGetProperty("body", out var body));
		Assert.AreEqual(JsonValueKind.Number, body.ValueKind);
		Assert.AreEqual(42, body.GetInt32());
	}

	[Test, Category(_projectionType)]
	public void raw_string_body_does_not_throw_and_yields_string() {
		var state = ProcessWithRaw("\"hello\"");
		var doc = JsonDocument.Parse(state);
		Assert.True(doc.RootElement.TryGetProperty("body", out var body));
		Assert.AreEqual(JsonValueKind.String, body.ValueKind);
		Assert.AreEqual("hello", body.GetString());
	}
}
