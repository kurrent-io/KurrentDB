// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using KurrentDB.Core.Time;
using Serilog;
using TrackerPoint = (long CommitPosition, System.DateTime Timestamp);

namespace KurrentDB.SecondaryIndexing.Diagnostics;

public class SecondaryIndexProgressTracker {
	private static readonly ILogger Log = Serilog.Log.Logger.ForContext<SecondaryIndexProgressTracker>();

	public SecondaryIndexProgressTracker(string indexName, Meter meter, long lastIndexedPosition, DateTime lastIndexedAt, IClock? clock = null) {
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
		InitLastIndexed((lastIndexedPosition, lastIndexedAt));
	}

	private TrackerPoint LastIndexed {
		[MethodImpl(MethodImplOptions.NoInlining)]
		get;
		[MethodImpl(MethodImplOptions.NoInlining)]
		set;
	} = (-1, DateTime.MinValue);

	private TrackerPoint LastAppended {
		[MethodImpl(MethodImplOptions.NoInlining)]
		get;
		[MethodImpl(MethodImplOptions.NoInlining)]
		set;
	} = (-1, DateTime.MinValue);

	private readonly KeyValuePair<string, object?>[] _tag;
	private readonly Histogram<double> _histogram;
	private readonly IClock _clock;
	private const string MeterPrefix = "indexes.secondary";

	public string IndexName { get; }

	public void InitLastAppended(TrackerPoint trackerPoint) {
		LastAppended = trackerPoint;
	}

	public void InitLastIndexed(TrackerPoint trackerPoint) {
		LastIndexed = trackerPoint;
	}

	public void RecordIndexed(TrackerPoint trackerPoint) {
		LastIndexed = trackerPoint;
	}

	public void RecordAppended(TrackerPoint trackerPoint) {
		LastAppended = trackerPoint;
	}

	public void RecordError(Exception e) {
		//TODO: Log error here
	}

	public CommitDuration StartCommitDuration() => new(_histogram, _clock, _tag[0], IndexName, Log);

	private IEnumerable<Measurement<long>> ObserveGap() {
		var lastAppendedPos = LastAppended.CommitPosition;
		var lastIndexedPos = LastIndexed.CommitPosition;

		if (lastAppendedPos < 0 || lastIndexedPos < 0)
			yield break;

		yield return new(lastAppendedPos - lastIndexedPos, _tag);
	}

	private IEnumerable<Measurement<double>> ObserveLag() {
		var lastAppendedAt = LastAppended.Timestamp.ToUniversalTime();
		var lastIndexedAt = LastIndexed.Timestamp.ToUniversalTime();

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
