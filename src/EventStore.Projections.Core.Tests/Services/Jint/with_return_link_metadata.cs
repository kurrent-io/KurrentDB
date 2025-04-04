// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using EventStore.Core.Data;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.Jint;

[TestFixture]
public class with_return_link_metadata : specification_with_event_handled {
	protected override void Given() {
		_projection = @"fromAll().when({$any:function(s,e){
                return e.linkMetadata;
            }})";
		_state = @"{}";
		_handledEvent = CreateSampleEvent("stream", 0, "event_type", "{\"data\":1}", new TFPos(100, 50));
	}

	[Test]
	public void returns_position_metadata_as_state() {
		Assert.AreEqual("{\"position_meta\":1}", _newState);
		Assert.IsTrue(_emittedEventEnvelopes == null || !_emittedEventEnvelopes.Any());
	}
}
