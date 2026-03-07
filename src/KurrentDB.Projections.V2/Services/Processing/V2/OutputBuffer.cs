// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class OutputBuffer {
	public List<EmittedEventEnvelope> EmittedEvents { get; } = new();
	public Dictionary<string, (string StreamName, string StateJson, long ExpectedVersion)> DirtyStates { get; } = new();
	public TFPos LastLogPosition { get; set; }

	public void AddEmittedEvents(EmittedEventEnvelope[] events) {
		if (events is { Length: > 0 })
			EmittedEvents.AddRange(events);
	}

	public void SetPartitionState(string partitionKey, string streamName, string stateJson, long expectedVersion) {
		DirtyStates[partitionKey] = (streamName, stateJson, expectedVersion);
	}

	public void Clear() {
		EmittedEvents.Clear();
		DirtyStates.Clear();
		LastLogPosition = default;
	}
}
