// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
using System.Collections.Generic;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Core.Index;

public interface IIndexTracker {
	void OnIndexed<TStreamId>(List<IPrepareLogRecord<TStreamId>> prepares);
}

public class IndexTracker : IIndexTracker {
	private readonly CounterSubMetric _indexedEvents;

	public IndexTracker(CounterSubMetric indexedEvents) {
		_indexedEvents = indexedEvents;
	}

	public void OnIndexed<TStreamId>(List<IPrepareLogRecord<TStreamId>> prepares) {
		_indexedEvents.Add(prepares.Count);
	}

	public class NoOp : IIndexTracker {
		public void OnIndexed<TStreamId>(List<IPrepareLogRecord<TStreamId>> record) {
		}
	}
}
