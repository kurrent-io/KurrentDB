// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.Jint;

[TestFixture]
public class when_round_tripping_uni_state_js_projection_with_string_state : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
                fromAll().when({
                    type1: function(state, event) {
                        return ""hello"";
                    }
                });
            ";
		_state = @"{}";
	}

	[Test, Category(_projectionType)]
	public void produced_state_is_json_encoded_so_it_round_trips() {
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 10, 5), "stream1", "type1", "category", Guid.NewGuid(), 0, "metadata",
			@"{}", out var producedState, out _, out _);

		Assert.AreEqual("\"hello\"", producedState,
			"String state must be JSON-encoded so Load() can parse it on restart.");

		var freshHandler = CreateStateHandler();
		Assert.DoesNotThrow(
			() => freshHandler.Load(producedState),
			$"Load() threw on the produced state value: <<{producedState}>>. Round-trip is broken.");
	}
}
