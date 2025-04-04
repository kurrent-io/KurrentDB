// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.Jint;

public class when_running_a_js_projection_emitting_invalid_links : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
                fromAll().when({$any: 
                    function(state, event) {
						event = {};
                    linkTo('output-stream', event);
                    return {};
                }});
            ";
	}

	[Test, Category(_projectionType)]
	public void process_event_does_not_allow_emitted_event() {
		var ex = Assert.Throws<Exception>(() => {
			_stateHandler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
				"metadata",
				@"{""a"":""b""}", out _, out var emittedEvents);
		});
		Assert.AreEqual("Invalid link to event undefined@undefined", ex.Message);
	}
}
