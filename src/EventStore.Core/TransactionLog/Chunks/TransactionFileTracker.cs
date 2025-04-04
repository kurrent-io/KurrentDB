// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
using System.Collections.Generic;
using EventStore.Core.Metrics;
using EventStore.Core.Time;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.TransactionLog.Chunks;

public class TFChunkTracker : ITransactionFileTracker {
	private readonly LogicalChunkReadDistributionMetric _readDistribution;
	private readonly DurationMetric _readDurationMetric;
	private readonly CounterSubMetric _readBytes;
	private readonly CounterSubMetric _readEvents;

	public TFChunkTracker(
		LogicalChunkReadDistributionMetric readDistribution,
		DurationMetric readDurationMetric,
		CounterSubMetric readBytes,
		CounterSubMetric readEvents) {

		_readDistribution = readDistribution;
		_readDurationMetric = readDurationMetric;
		_readBytes = readBytes;
		_readEvents = readEvents;
	}

	public void OnRead(Instant start, ILogRecord record, ITransactionFileTracker.Source source) {
		_readDistribution.Record(record);

		_readDurationMetric.Record(
			start,
			new KeyValuePair<string, object>("source", source),
			new KeyValuePair<string, object>("user", "")); // placeholder for later

		if (record is not PrepareLogRecord prepare)
			return;

		_readBytes.Add(prepare.Data.Length + prepare.Metadata.Length);
		_readEvents.Add(1);
	}

	public class NoOp : ITransactionFileTracker {
		public void OnRead(Instant start, ILogRecord record, ITransactionFileTracker.Source source) {
		}
	}
}
