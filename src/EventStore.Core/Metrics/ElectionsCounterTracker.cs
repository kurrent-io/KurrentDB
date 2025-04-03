// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Messages;


namespace EventStore.Core.Metrics;

public interface IElectionCounterTracker : IHandle<ElectionMessage.ElectionsDone> {
}

public class ElectionsCounterTracker : IElectionCounterTracker {
	private readonly CounterSubMetric _electionsCounter;

	public ElectionsCounterTracker(CounterSubMetric electionsCounter) {
		_electionsCounter = electionsCounter;
	}

	public void Handle(ElectionMessage.ElectionsDone message) {
		_electionsCounter.Add(1);
	}

	public class NoOp : IElectionCounterTracker {
		public void Handle(ElectionMessage.ElectionsDone message) {
		}
	}
}
