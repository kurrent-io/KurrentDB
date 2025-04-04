// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
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
