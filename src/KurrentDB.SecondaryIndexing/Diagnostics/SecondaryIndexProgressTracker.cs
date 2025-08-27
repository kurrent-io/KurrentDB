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
	void InitLastAppended(Event eventRecord);
	void RecordAppended(EventRecord eventRecord, long commitPosition);
	void RecordCommit(Func<(TFPos position, int batchSize)> callback);
	void RecordError(Exception e);
}

public class NoOpSecondaryIndexProgressTracker : ISecondaryIndexProgressTracker {
	public void RecordIndexed(ResolvedEvent resolvedEvent) {
	}

	public void InitLastAppended(Event eventRecord) {
	}

	public void RecordAppended(EventRecord eventRecord, long commitPosition) {
	}

	public void RecordCommit(Func<(TFPos position, int batchSize)> callback) {
		callback();
	}

	public void RecordError(Exception e) {
	}
}

public class SecondaryIndexProgressTracker : ISecondaryIndexProgressTracker {
	private static readonly ILogger Log = Serilog.Log.Logger.ForContext<SecondaryIndexProgressTracker>();
	private long _lastIndexedPosition = -1;
	private long _lastIndexedAt = -1;

	private long _initLastAppendedPosition = -1;
	private long _initLastAppendedAt = -1;

	private long _lastAppendedPosition = -1;
	private long _lastAppendedAt = -1;

	private int _numUncommittedIndexEvents;
	private long _lastIndexCommitPosition = -1;

	private readonly SecondaryIndexTrackers _trackers = new();
	private readonly Stopwatch _sw = new();


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
	}

	public void RecordIndexed(ResolvedEvent resolvedEvent) {
		Interlocked.Exchange(ref _lastIndexedPosition, resolvedEvent.OriginalPosition!.Value.CommitPosition);
		Interlocked.Exchange(ref _lastIndexedAt, DateTime.UtcNow.Ticks);
		Interlocked.Increment(ref _numUncommittedIndexEvents);
	}

	public void InitLastAppended(Event @event) {
		Interlocked.Exchange(ref _initLastAppendedPosition, (long)@event.CommitPosition);
		Interlocked.Exchange(ref _initLastAppendedAt, @event.Created.Ticks);
	}

	public void RecordAppended(EventRecord record, long commitPosition) {
		if (record.EventType.StartsWith('$') || record.EventStreamId.StartsWith('$')) {
			// ignore system events
			return;
		}

		Interlocked.Exchange(ref _lastAppendedPosition, commitPosition);
		Interlocked.Exchange(ref _lastAppendedAt, record.TimeStamp.Ticks);
	}

	public void RecordCommit(Func<(TFPos position, int batchSize)> callback) {
		using var duration = _trackers.Commit.Start();
		_sw.Restart();
		var (position, batchSize) = callback();
		Interlocked.Exchange(ref _lastIndexCommitPosition, position.CommitPosition);
		Interlocked.Exchange(ref _numUncommittedIndexEvents, 0);
		_sw.Stop();

		Log.Debug("Committed {Count} records to index at log positon {Seq} ({Took} ms)", batchSize, position, _sw.ElapsedMilliseconds);
	}

	public void RecordError(Exception e) {
		//TODO: Log error here
	}

	private IEnumerable<Measurement<long>> ObserveGap() {
		var lastAppendedPos = Math.Max(Interlocked.Read(ref _initLastAppendedPosition), Interlocked.Read(ref _lastAppendedPosition));
		var lastIndexedPos = Interlocked.Read(ref _lastIndexedPosition);

		if (lastAppendedPos < 0 || lastIndexedPos < 0)
			yield break;

		yield return new(lastAppendedPos - lastIndexedPos);
	}

	private IEnumerable<Measurement<long>> ObserveLag() {
		var lastAppendedAt = Math.Max(Interlocked.Read(ref _initLastAppendedAt), Interlocked.Read(ref _lastAppendedAt));
		var lastIndexedAt = Interlocked.Read(ref _lastIndexedAt);

		if (lastAppendedAt < 0 || lastIndexedAt < 0)
			yield break;

		var lag = (long)new TimeSpan(lastIndexedAt - lastAppendedAt).TotalMilliseconds;

		yield return new(lag);
	}

	private Measurement<int> ObservePending() {
		return new(_numUncommittedIndexEvents);
	}
}
