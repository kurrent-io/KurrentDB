// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using KurrentDB.Common.Configuration;
using KurrentDB.Kontext.Indexing;

namespace KurrentDB.Kontext.Diagnostics;

public sealed class KontextProgressTracker {
	const string MeterPrefix = "kontext.indexing";

	readonly IndexingStatus _status;
	readonly Func<long> _writerCheckpoint;
	readonly TimeProvider _clock;
	readonly Histogram<double> _indexHistogram;
	readonly Histogram<double> _commitHistogram;
	readonly KeyValuePair<string, object?>[] _ftsTags;
	readonly KeyValuePair<string, object?>[] _embeddingTags;

	public KontextProgressTracker(
		string workspaceName,
		IndexingStatus status,
		Meter meter,
		Func<long> writerCheckpoint,
		MetricsConfiguration? metricsConfiguration = null,
		TimeProvider? clock = null) {
		_status = status;
		_writerCheckpoint = writerCheckpoint;
		_clock = clock ?? TimeProvider.System;

		_ftsTags = [new("workspace", workspaceName), new("pipeline", KontextConstants.FtsPipeline)];
		_embeddingTags = [new("workspace", workspaceName), new("pipeline", KontextConstants.EmbeddingPipeline)];

		var serviceName = metricsConfiguration?.ServiceName ?? "kurrentdb";

		meter.CreateObservableGauge(
			$"{serviceName}.{MeterPrefix}.gap",
			ObserveGap,
			"bytes",
			"Distance between the last indexed event and the writer's tail, in bytes"
		);

		meter.CreateObservableGauge(
			$"{serviceName}.{MeterPrefix}.lag",
			ObserveLag,
			"s",
			"Time since the last indexed event was originally appended, in seconds"
		);

		_indexHistogram = meter.CreateHistogram<double>(
			$"{serviceName}.{MeterPrefix}.index.seconds",
			advice: new() { HistogramBucketBoundaries = MetricsConfiguration.SecondsHistogramBucketConfiguration.Boundaries }
		);

		_commitHistogram = meter.CreateHistogram<double>(
			$"{serviceName}.{MeterPrefix}.commit.seconds",
			advice: new() { HistogramBucketBoundaries = MetricsConfiguration.SecondsHistogramBucketConfiguration.Boundaries }
		);
	}

	public Scope StartFtsIndex() => new(_indexHistogram, _clock, _ftsTags);
	public Scope StartEmbeddingIndex() => new(_indexHistogram, _clock, _embeddingTags);
	public Scope StartFtsCommit() => new(_commitHistogram, _clock, _ftsTags);
	public Scope StartEmbeddingCommit() => new(_commitHistogram, _clock, _embeddingTags);

	IEnumerable<Measurement<long>> ObserveGap() {
		var tail = _writerCheckpoint();
		if (tail <= 0)
			yield break;

		var fts = (long)_status.FtsPosition;
		if (fts > 0)
			yield return new(Math.Max(0, tail - fts), _ftsTags);

		var emb = (long)_status.EmbeddingPosition;
		if (emb > 0)
			yield return new(Math.Max(0, tail - emb), _embeddingTags);
	}

	IEnumerable<Measurement<double>> ObserveLag() {
		var now = _clock.GetUtcNow().UtcDateTime;

		if (_status.FtsLastIndexedAt is { } ftsAt)
			yield return new(Math.Max(0, (now - ftsAt).TotalSeconds), _ftsTags);

		if (_status.EmbeddingLastIndexedAt is { } embAt)
			yield return new(Math.Max(0, (now - embAt).TotalSeconds), _embeddingTags);
	}

	public sealed class Scope(
		Histogram<double> histogram,
		TimeProvider clock,
		KeyValuePair<string, object?>[] tags) : IDisposable {
		readonly long _start = clock.GetTimestamp();

		public void Dispose() =>
			histogram.Record(clock.GetElapsedTime(_start).TotalSeconds, tags);
	}
}
