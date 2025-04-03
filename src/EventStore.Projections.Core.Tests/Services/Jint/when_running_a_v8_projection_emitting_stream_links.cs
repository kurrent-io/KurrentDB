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
public class when_running_a_v8_projection_emitting_stream_links : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
                fromAll().when({$any: 
                    function(state, event) {
                    linkStreamTo('output-stream' + event.sequenceNumber, 'stream' + event.sequenceNumber);
                    return {};
                }});
            ";
	}

	[Test, Category(_projectionType)]
	public void process_event_returns_true() {
		string state;
		EmittedEventEnvelope[] emittedEvents;
		var result = _stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
			"metadata",
			@"{""a"":""b""}", out state, out emittedEvents);

		Assert.IsTrue(result);
	}

	[Test, Category(_projectionType)]
	public void process_event_returns_emitted_event() {
		string state;
		EmittedEventEnvelope[] emittedEvents;
		_stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
			"metadata",
			@"{""a"":""b""}", out state, out emittedEvents);

		Assert.IsNotNull(emittedEvents);
		Assert.AreEqual(1, emittedEvents.Length);
		Assert.AreEqual("$@", emittedEvents[0].Event.EventType);
		Assert.AreEqual("output-stream0", emittedEvents[0].Event.StreamId);
		Assert.AreEqual("stream0", emittedEvents[0].Event.Data);
	}
}
