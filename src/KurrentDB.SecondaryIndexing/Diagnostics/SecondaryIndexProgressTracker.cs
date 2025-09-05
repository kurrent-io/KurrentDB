// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using KurrentDB.Core.Data;
using KurrentDB.Core.Time;
using Serilog;

namespace KurrentDB.SecondaryIndexing.Diagnostics;

public class SecondaryIndexProgressTracker {
	private static readonly ILogger Log = Serilog.Log.Logger.ForContext<SecondaryIndexProgressTracker>();

	private long LastIndexedPosition {
		[MethodImpl(MethodImplOptions.NoInlining)]
		get;
		[MethodImpl(MethodImplOptions.NoInlining)]
		set;
	} = -1;

	private DateTime LastIndexedAt {
		[MethodImpl(MethodImplOptions.NoInlining)]
		get;
		[MethodImpl(MethodImplOptions.NoInlining)]
		set;
	} = DateTime.MinValue;

	private long LastAppendedPosition {
		[MethodImpl(MethodImplOptions.NoInlining)]
		get;
		[MethodImpl(MethodImplOptions.NoInlining)]
		set;
	} = -1;

	private DateTime LastAppendedAt {
		[MethodImpl(MethodImplOptions.NoInlining)]
		get;
		[MethodImpl(MethodImplOptions.NoInlining)]
		set;
	} = DateTime.MinValue;

	private readonly KeyValuePair<string, object?>[] _tag;
	private readonly Histogram<double> _histogram;
	private readonly IClock _clock;

	private const string MeterPrefix = "indexes.secondary";

	public SecondaryIndexProgressTracker(string indexName, Meter meter, IClock? clock = null) {
		_clock = clock ?? Clock.Instance;
		IndexName = indexName;

		meter.CreateObservableGauge(
			$"{MeterPrefix}.subscription.gap",
			ObserveGap,
			"bytes",
			"Gap between last indexed and current last event log position, in bytes"
		);

		meter.CreateObservableGauge(
			$"{MeterPrefix}.subscription.lag",
			ObserveLag,
			"s",
			"Time between last appended and last indexed event, in seconds"
		);

		_histogram = meter.CreateHistogram<double>($"{MeterPrefix}.commit.seconds");
		_tag = [new("index", indexName)];
	}

	public string IndexName { get; }

	public void RecordIndexed(ref ResolvedEvent resolvedEvent) {
		LastIndexedPosition = resolvedEvent.OriginalPosition!.Value.CommitPosition;
		LastIndexedAt = DateTime.Now;
	}

	public void InitLastAppended(ref ResolvedEvent resolvedEvent) {
		LastAppendedPosition = resolvedEvent.EventPosition!.Value.CommitPosition;
		LastAppendedAt = resolvedEvent.OriginalEvent.TimeStamp;
	}

	public void RecordAppended(EventRecord record, long commitPosition) {
		LastAppendedPosition = commitPosition;
		LastAppendedAt = record.TimeStamp;
	}

	public void RecordError(Exception e) {
		//TODO: Log error here
	}

	public CommitDuration StartCommitDuration() => new(_histogram, _clock, _tag[0], IndexName, Log);

	private IEnumerable<Measurement<long>> ObserveGap() {
		var lastAppendedPos = LastAppendedPosition;
		var lastIndexedPos = LastIndexedPosition;

		if (lastAppendedPos < 0 || lastIndexedPos < 0)
			yield break;

		yield return new(lastAppendedPos - lastIndexedPos, _tag);
	}

	private IEnumerable<Measurement<double>> ObserveLag() {
		var lastAppendedAt = LastAppendedAt;
		var lastIndexedAt = LastIndexedAt;

		if (lastAppendedAt == DateTime.MinValue || lastIndexedAt == DateTime.MinValue)
			yield break;

		var lag = (lastIndexedAt - lastAppendedAt).TotalSeconds;

		yield return new(lag, _tag);
	}

	public sealed class CommitDuration(Histogram<double> histogram, IClock clock, KeyValuePair<string, object?> tag, string indexName, ILogger logger) : IDisposable {
		private readonly Instant _start = clock.Now;

		public void Dispose() {
			var stop = clock.Now;
			var elapsed = stop.ElapsedSecondsSince(_start);
			logger.Debug("Secondary index {Index} records committed in {Duration} ms", indexName, elapsed * 1000);
			histogram.Record(elapsed, tag);
		}
	}
}
