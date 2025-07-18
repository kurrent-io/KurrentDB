// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Diagnostics.Metrics;
using KurrentDB.Core.Data;
using KurrentDB.Core.Metrics;
using Serilog;
using Event = KurrentDB.POC.IO.Core.Event;

namespace KurrentDB.SecondaryIndexing.Indexes.Diagnostics;

public interface ISecondaryIndexProgressTracker {
	void RecordIndexed(ResolvedEvent resolvedEvent);
	void RecordAppended(Event eventRecord);
	void RecordAppended(EventRecord eventRecord);
	void RecordCommit(Func<(long position, int batchSize)> callback);
	void RecordError(Exception e);
}

public class NoOpSecondaryIndexProgressTracker : ISecondaryIndexProgressTracker {
	public void RecordIndexed(ResolvedEvent resolvedEvent) {
	}

	public void RecordAppended(Event eventRecord) {
	}

	public void RecordAppended(EventRecord eventRecord) {
	}

	public void RecordCommit(Func<(long position, int batchSize)> callback) {
		callback();
	}

	public void RecordError(Exception e) {
	}
}

public class SecondaryIndexProgressTracker : ISecondaryIndexProgressTracker {
	private static readonly ILogger Log = Serilog.Log.Logger.ForContext<SecondaryIndexProgressTracker>();
	private long _lastIndexedPosition = -1;
	private long _lastCommittedPosition = -1;
	private long _lastLogPosition = -1;
	private long _lastIndexedAt = -1;
	private long _lastAppendedAt = -1;
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
			"bytes",
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

	public void RecordAppended(Event eventRecord) =>
		RecordAppended(
			eventRecord.Stream,
			eventRecord.EventType,
			(long)eventRecord.PreparePosition,
			eventRecord.Created.Ticks
		);

	public void RecordAppended(EventRecord eventRecord) =>
		RecordAppended(
			eventRecord.EventStreamId,
			eventRecord.EventType,
			eventRecord.LogPosition,
			eventRecord.TimeStamp.Ticks
		);

	private void RecordAppended(string streamId, string eventType, long logPosition, long timestampTicks) {
		try {
			if (eventType.StartsWith('$') || streamId.StartsWith('$')) {
				// ignore system events
				return;
			}

			var currentLasLogPosition = Interlocked.Read(ref _lastLogPosition);

			// Just in case we have some race condition between reading last event and getting a newly appended notification
			if (currentLasLogPosition >= logPosition)
				return;

			if (Interlocked.CompareExchange(ref _lastLogPosition, logPosition, currentLasLogPosition) ==
			    currentLasLogPosition) {
				Interlocked.Exchange(ref _lastAppendedAt, timestampTicks);
			}
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

	private IEnumerable<Measurement<long>> ObserveGap() {
		var streamPos = Interlocked.Read(ref _lastLogPosition);
		var indexedPos = Interlocked.Read(ref _lastIndexedPosition);

		if(streamPos == -1 || indexedPos == -1)
			yield break;

		yield return new Measurement<long>(streamPos - indexedPos);
	}

	private IEnumerable<Measurement<long>> ObserveLag() {
		var lastAppendedAt = Interlocked.Read(ref _lastAppendedAt);
		var lastIndexedAt = Interlocked.Read(ref _lastIndexedAt);

		if(lastAppendedAt == -1 || lastIndexedAt == -1)
			yield break;

		var lag = (lastAppendedAt - lastIndexedAt) / 1000;

		yield return new Measurement<long>(lag);
	}

	private Measurement<int> ObservePending() {
		return new Measurement<int>(_pendingEvents);
	}
}
