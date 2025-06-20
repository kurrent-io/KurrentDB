// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using KurrentDB.Core.Data;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indexes.Diagnostics;

public interface ISecondaryIndexProgressTracker {
	void RecordIndexed(ResolvedEvent resolvedEvent);
	void RecordAppended(EventRecord eventRecord);
	void RecordCommit(long position, int batchSize, long duration);
	void RecordError(Exception e);
}

public class NoOpSecondaryIndexProgressTracker : ISecondaryIndexProgressTracker {
	public void RecordIndexed(ResolvedEvent resolvedEvent) {
	}

	public void RecordAppended(EventRecord eventRecord) {
	}

	public void RecordCommit(long position, int batchSize, long duration) {
	}

	public void RecordError(Exception e) {
	}
}

public class SecondaryIndexProgressTracker : ISecondaryIndexProgressTracker {
	private static readonly ILogger Log = Serilog.Log.Logger.ForContext<SecondaryIndexProgressTracker>();
	private long _lastIndexedPosition;
	private long _lastCommittedPosition;
	private long _lastLogPosition;
	private long _lastIndexedAt;
	private long _lastAppendedAt;
	private int _pendingEvents;

	public SecondaryIndexProgressTracker(Meter meter, string meterPrefix) {
		meter.CreateObservableGauge(
			$"{meterPrefix}.subscription.gap",
			ObserveGap,
			"events",
			"Gap between last indexed and current last event log position"
		);

		meter.CreateObservableGauge(
			$"{meterPrefix}.subscription.lag",
			ObserveLag,
			"s",
			"Time between last appended and last indexed event"
		);

		meter.CreateObservableGauge(
			$"{meterPrefix}.subscription.pending",
			ObservePending,
			"events",
			"Events pending checkpoint"
		);
	}

	public void RecordIndexed(ResolvedEvent resolvedEvent) {
		try {
			Interlocked.Exchange(ref _lastIndexedPosition, resolvedEvent.OriginalEvent.LogPosition);
			Interlocked.Exchange(ref _lastIndexedAt, DateTime.UtcNow.Ticks);
			Interlocked.Increment(ref _pendingEvents);
		} catch (Exception exc) {
			Log.Error(exc, "Error recording metrics of event indexed in secondary index.");
		}
	}

	public void RecordAppended(EventRecord eventRecord) {
		try {
			Interlocked.Exchange(ref _lastLogPosition, eventRecord.LogPosition);
			Interlocked.Exchange(ref _lastAppendedAt, eventRecord.TimeStamp.Ticks);
		} catch (Exception exc) {
			Log.Error(exc, "Error recording metrics of event appended.");
		}
	}

	public void RecordCommit(long position, int batchSize, long duration) {
		try {
			Interlocked.Exchange(ref _lastCommittedPosition, position);
			Interlocked.Exchange(ref _pendingEvents, 0);
		} catch (Exception exc) {
			Log.Error(exc, "Error recording metrics of event appended.");
		}
	}

		public void RecordError(Exception e) {
			//TODO: Log error here
		}

		private Measurement<long> ObserveGap() {
			var streamPos = Interlocked.Read(ref _lastLogPosition);
			var indexedPos = Interlocked.Read(ref _lastIndexedPosition);

			return new Measurement<long>(streamPos - indexedPos);
		}

		private Measurement<long> ObserveLag() {
			var lastAppendedAt = Interlocked.Read(ref _lastAppendedAt);
			var lastIndexedAt = Interlocked.Read(ref _lastIndexedAt);
			var lag = lastAppendedAt - lastIndexedAt;

			return new Measurement<long>(lag);
		}

		private Measurement<int> ObservePending() {
			return new Measurement<int>(_pendingEvents);
		}
	}
