// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using KurrentDB.Common.Configuration;
using KurrentDB.Core.Data;
using Microsoft.Extensions.Logging;

namespace KurrentDB.SecondaryIndexing.Diagnostics;

public delegate long GetLastPosition();

public class SecondaryIndexProgressTracker {
	private readonly KeyValuePair<string, object?>[] _tag;
	private readonly Histogram<double> _histogram;
	private readonly TimeProvider _clock;
	private readonly ILogger _log;
	private readonly string _indexName;

	private long _lastIndexedPosition = -1;
	private DateTime _lastIndexedTimestamp = DateTime.MinValue;
	private long _lastAppendedPosition = -1;
	private DateTime _lastAppendedTimestamp = DateTime.MinValue;

	private const string MeterPrefix = "indexes.secondary";

	public SecondaryIndexProgressTracker(
		string indexName,
		string serviceName,
		Meter meter,
		TimeProvider clock,
		ILogger log
	) {
		_clock = clock;
		_log = log;
		_indexName = indexName;

		meter.CreateObservableGauge(
			$"{serviceName}.{MeterPrefix}.gap",
			ObserveGap,
			"bytes",
			"Gap between last indexed log record and the tail of the log, in bytes"
		);

		meter.CreateObservableGauge(
			$"{serviceName}.{MeterPrefix}.lag",
			ObserveLag,
			"s",
			"Time taken between appending an log record and indexing it, in seconds"
		);

		_histogram = meter.CreateHistogram<double>(
			$"{serviceName}.{MeterPrefix}.commit.seconds",
			advice: new() { HistogramBucketBoundaries = MetricsConfiguration.SecondsHistogramBucketConfiguration.Boundaries }
		);
		_tag = [new("index", indexName)];
	}

	public void RecordAppended(EventRecord eventRecord, long commitPosition) {
		_lastAppendedPosition = commitPosition;
		_lastAppendedTimestamp = eventRecord.TimeStamp;
	}

	public void InitLastIndexed(long commitPosition, DateTimeOffset timestamp) {
		_lastIndexedPosition = commitPosition;
		_lastIndexedTimestamp = timestamp.LocalDateTime;
	}

	public void RecordIndexed(ResolvedEvent resolvedEvent) {
		_lastIndexedPosition = resolvedEvent.OriginalPosition!.Value.CommitPosition;
		_lastIndexedTimestamp = resolvedEvent.OriginalEvent.TimeStamp;
	}

	public CommitDuration StartCommitDuration(int count) => new(_histogram, _clock, _tag[0], _indexName, _log, count);

	private IEnumerable<Measurement<long>> ObserveGap() {
		var lastAppendedPos = _lastAppendedPosition;
		var lastIndexedPos = _lastIndexedPosition;

		if (lastAppendedPos < 0 || lastIndexedPos < 0)
			yield break;

		yield return new(lastAppendedPos - lastIndexedPos, _tag);
	}

	private IEnumerable<Measurement<double>> ObserveLag() {
		if (_lastAppendedTimestamp == DateTime.MinValue || _lastIndexedTimestamp == DateTime.MinValue)
			yield break;

		var lag = _lastAppendedTimestamp - _lastIndexedTimestamp;
		yield return new(lag.TotalSeconds, _tag);
	}

	public sealed class CommitDuration(
		Histogram<double> histogram,
		TimeProvider clock,
		KeyValuePair<string, object?> tag,
		string indexName,
		ILogger logger,
		int count) : IDisposable {
		private readonly long _start = clock.GetTimestamp();

		public void Dispose() {
			var elapsed = clock.GetElapsedTime(_start).Milliseconds;
			logger.LogDebug("Secondary index [{Index}]: {Count} records committed in {Duration} ms", indexName, count, elapsed);
			histogram.Record(elapsed, tag);
		}
	}
}
