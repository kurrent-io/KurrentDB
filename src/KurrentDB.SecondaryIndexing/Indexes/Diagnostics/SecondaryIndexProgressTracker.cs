// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Diagnostics.Metrics;
using KurrentDB.Core.Data;
using KurrentDB.Core.Metrics;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Indexes.Diagnostics;

public interface ISecondaryIndexProgressTracker {
	void RecordIndexed(ResolvedEvent resolvedEvent);
	void RecordAppended(EventRecord eventRecord);

	void RecordCommit(Func<(long position, int batchSize)> callback);
	void RecordError(Exception e);
}

public class NoOpSecondaryIndexProgressTracker : ISecondaryIndexProgressTracker {
	public void RecordIndexed(ResolvedEvent resolvedEvent) {
	}

	public void RecordAppended(EventRecord eventRecord) {
	}

	public void RecordCommit(Func<(long position, int batchSize)> callback) {
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
	private readonly SecondaryIndexTrackers _trackers = new();
	private readonly Stopwatch _sw = new();

	public SecondaryIndexProgressTracker(
		Meter meter,
		string meterPrefix
	) {
		meter.CreateObservableGauge(
			$"{meterPrefix}.subscription.gap",
			ObserveGap,
			"events",
			"Gap between last indexed and current last event log position"
		);

		meter.CreateObservableGauge(
			$"{meterPrefix}.subscription.lag",
			ObserveLag,
			"ms",
			"Time between last appended and last indexed event"
		);

		meter.CreateObservableGauge(
			$"{meterPrefix}.subscription.pending",
			ObservePending,
			"events",
			"Events pending checkpoint"
		);

		var commitLatencyTracker = new DurationMetric(meter, $"{meterPrefix}.commit", false);

		_trackers.Commit = new DurationTracker(commitLatencyTracker, "commit");
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
			if (eventRecord.EventType.StartsWith('$') || eventRecord.EventStreamId.StartsWith('$')) {
				// ignore system events
				return;
			}

			Interlocked.Exchange(ref _lastLogPosition, eventRecord.LogPosition);
			Interlocked.Exchange(ref _lastAppendedAt, eventRecord.TimeStamp.Ticks);
		} catch (Exception exc) {
			Log.Error(exc, "Error recording metrics of event appended.");
		}
	}

	public void RecordCommit(Func<(long position, int batchSize)> callback) {
		using var duration = _trackers.Commit.Start();
		try {
			_sw.Restart();
			var (position, batchSize) = callback();
			Interlocked.Exchange(ref _lastCommittedPosition, position);
			Interlocked.Exchange(ref _pendingEvents, 0);
			_sw.Stop();

			Log.Debug("Committed {Count} records to index at seq {Seq} ({Took} ms)",
				batchSize, position, _sw.ElapsedMilliseconds);
		} catch (Exception exc) {
			Log.Error(exc, "Error recording metrics of event appended.");
			throw;
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
		var lag = (lastAppendedAt - lastIndexedAt) / 1000;

		return new Measurement<long>(lag);
	}

	private Measurement<int> ObservePending() {
		return new Measurement<int>(_pendingEvents);
	}
}
