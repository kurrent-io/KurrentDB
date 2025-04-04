// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.Jint;

public class when_specifying_meta_data_for_linked_event : TestFixtureWithInterpretedProjection {
	protected override void Given() {
		_projection = @"
            fromAll().when({$any: 
                function(state, event) {
                linkTo('output-stream', event, {'meta': 'data'});
                return {};
            }});
        ";
	}

	[Test, Category(_projectionType)]
	public void meta_data_should_be_set() {
		string state = null;
		EmittedEventEnvelope[] emittedEvents = null;

		var result = _stateHandler.ProcessEvent(
			"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
			"metadata",
			null, out state, out emittedEvents, isJson: false);

		Assert.IsNotNull(emittedEvents);
		Assert.AreEqual(1, emittedEvents.Length);
		Assert.IsNotNull(emittedEvents[0].Event);

		var metaData = emittedEvents[0].Event.ExtraMetaData();
		CollectionAssert.AreEquivalent(new Dictionary<string, string> { { "meta", "\"data\"" } }, metaData);
	}
}
