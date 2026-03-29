// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public interface IReadOnlyOutputBuffer {
	IReadOnlyList<EmittedEventEnvelope> EmittedEvents { get; }
	IReadOnlyDictionary<string, (string StreamName, string StateJson, long ExpectedVersion)> DirtyStates { get; }
	TFPos LastLogPosition { get; }
}

public class OutputBuffer : IReadOnlyOutputBuffer {
	private readonly List<EmittedEventEnvelope> _emittedEvents = [];
	private readonly Dictionary<string, (string StreamName, string StateJson, long ExpectedVersion)> _dirtyStates = [];

	public IReadOnlyList<EmittedEventEnvelope> EmittedEvents => _emittedEvents;
	public IReadOnlyDictionary<string, (string StreamName, string StateJson, long ExpectedVersion)> DirtyStates => _dirtyStates;
	public TFPos LastLogPosition { get; set; }

	public void AddEmittedEvents(EmittedEventEnvelope[] events) {
		if (events is { Length: > 0 })
			_emittedEvents.AddRange(events);
	}

	public void SetPartitionState(string partitionKey, string streamName, string stateJson, long expectedVersion) {
		_dirtyStates[partitionKey] = (streamName, stateJson, expectedVersion);
	}

	public void Clear() {
		_emittedEvents.Clear();
		_dirtyStates.Clear();
		LastLogPosition = default;
	}
}
