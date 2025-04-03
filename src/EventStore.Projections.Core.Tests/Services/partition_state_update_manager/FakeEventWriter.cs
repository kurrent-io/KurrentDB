// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace EventStore.Projections.Core.Tests.Services.partition_state_update_manager;

class FakeEventWriter : IEventWriter {
	private readonly List<EmittedEvent[]> _writes = new List<EmittedEvent[]>();

	public List<EmittedEvent[]> Writes {
		get { return _writes; }
	}

	public void ValidateOrderAndEmitEvents(EmittedEventEnvelope[] events) {
		Writes.Add(events.Select(v => v.Event).ToArray());
	}
}
