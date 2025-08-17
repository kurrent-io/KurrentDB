// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Diagnostics.Metrics;
using KurrentDB.Core.Data;
using KurrentDB.Core.Metrics;
using Serilog;
using Event = KurrentDB.POC.IO.Core.Event;

namespace KurrentDB.SecondaryIndexing.Diagnostics;

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
	static readonly ILogger Log = Serilog.Log.Logger.ForContext<SecondaryIndexProgressTracker>();
	long _lastIndexedPosition = -1;
	long _lastCommittedPosition = -1;
	long _lastLogPosition = -1;
	long _lastIndexedAt = -1;
	long _lastAppendedAt = -1;
	int _pendingEvents;
	readonly SecondaryIndexTrackers _trackers = new();
	readonly Stopwatch _sw = new();

	public SecondaryIndexProgressTracker(Meter meter, string meterPrefix) {
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
		return;

		Measurement<int> ObservePending() => new(_pendingEvents);

		IEnumerable<Measurement<long>> ObserveGap() {
			var streamPos = Interlocked.Read(ref _lastLogPosition);
			var indexedPos = Interlocked.Read(ref _lastIndexedPosition);

			if (streamPos == -1 || indexedPos == -1)
				yield break;

			yield return new(streamPos - indexedPos);
		}

		IEnumerable<Measurement<long>> ObserveLag() {
			var lastAppendedAt = Interlocked.Read(ref _lastAppendedAt);
			var lastIndexedAt = Interlocked.Read(ref _lastIndexedAt);

			if (lastAppendedAt == -1 || lastIndexedAt == -1)
				yield break;

			var lag = (long)new TimeSpan(lastIndexedAt - lastAppendedAt).TotalMilliseconds;

			yield return new(lag);
		}
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

	void RecordAppended(string streamId, string eventType, long logPosition, long timestampTicks) {
		try {
			if (eventType.StartsWith('$') || streamId.StartsWith('$')) {
				// ignore system events
				return;
			}

			var currentLastLogPosition = Interlocked.Read(ref _lastLogPosition);

			// Just in case we have some race condition between reading the last event and getting a newly appended notification
			if (currentLastLogPosition >= logPosition)
				return;

			if (Interlocked.CompareExchange(ref _lastLogPosition, logPosition, currentLastLogPosition) == currentLastLogPosition) {
				Interlocked.Exchange(ref _lastAppendedAt, timestampTicks);
			}
		} catch (Exception exc) {
			Log.Error(exc, "Error recording metrics of event appended.");
		}
	}

	public void RecordCommit(Func<(long position, int batchSize)> callback) {
		using var duration = _trackers.Commit.Start();
		_sw.Restart();
		var (position, batchSize) = callback();
		Interlocked.Exchange(ref _lastCommittedPosition, position);
		Interlocked.Exchange(ref _pendingEvents, 0);
		_sw.Stop();

		Log.Debug("Committed {Count} records to index at log positon {Seq} ({Took} ms)", batchSize, position, _sw.ElapsedMilliseconds);
	}

	public void RecordError(Exception e) {
		//TODO: Log error here
	}
}
