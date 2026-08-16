// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using DotNext.Threading;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Diagnostics;

/// <summary>
/// Emits progress telemetry for anything trailing a monotonic source: a gap gauge (head position
/// minus last processed position), a lag gauge (head timestamp minus last processed timestamp,
/// in seconds), and a commit-duration histogram. The gauges are pull-based — evaluated on metrics
/// scrape and silent until both sides of the subtraction have a value.
/// </summary>
public sealed class ProjectionProgressTracker {
    readonly KeyValuePair<string, object?>[] _tags;
    readonly Histogram<double> _histogram;
    readonly TimeProvider _clock;
    readonly string _name;
    readonly Func<ProgressMark> _getHead;
    readonly ILogger<ProjectionProgressTracker> _log;

    Atomic<ProgressMark> _lastProcessed;

    public ProjectionProgressTracker(string serviceName, ProjectionProgressTrackerOptions options, Meter meter, TimeProvider clock, ILogger<ProjectionProgressTracker> log) {
        _clock   = clock;
        _name    = options.Name;
        _getHead = options.GetHead;
        _log     = log;
        _tags    = [new(options.TagKey, options.Name)];

        _lastProcessed.Value = ProgressMark.Unset;

        meter.CreateObservableGauge(
            $"{serviceName}.{options.Scope}.gap",
            ObserveGap,
            options.GapUnit,
            "Distance between the head of the source and the last processed record");

        meter.CreateObservableGauge(
            $"{serviceName}.{options.Scope}.lag",
            ObserveLag,
            "s",
            "Time between a record reaching the head of the source and it being processed, in seconds");

        _histogram = meter.CreateHistogram<double>(
            $"{serviceName}.{options.Scope}.commit.seconds",
            advice: new() { HistogramBucketBoundaries = options.CommitSecondsBuckets });
    }

    public void RecordProcessed(ProgressMark mark) => _lastProcessed.Value = mark;

    public CommitScope StartCommit() => new(_histogram, _clock, _tags[0], _name, _log);

    IEnumerable<Measurement<long>> ObserveGap() {
        var head      = _getHead();
        var processed = _lastProcessed.Value;

        if (head.IsUnset || processed.IsUnset)
            yield break;

        yield return new(head.Position - processed.Position, _tags);
    }

    IEnumerable<Measurement<double>> ObserveLag() {
        var head      = _getHead();
        var processed = _lastProcessed.Value;

        if (head.IsUnset || processed.IsUnset)
            yield break;

        yield return new((head.Timestamp - processed.Timestamp).TotalSeconds, _tags);
    }

    public sealed class CommitScope(
        Histogram<double> histogram,
        TimeProvider clock,
        KeyValuePair<string, object?> tag,
        string name,
        ILogger log) : IDisposable {
        readonly long _start = clock.GetTimestamp();

        public void Dispose() {
            var elapsed = clock.GetElapsedTime(_start);
            log.LogProgressRecordsCommitted(name, elapsed.TotalMilliseconds);
            histogram.Record(elapsed.TotalSeconds, tag);
        }
    }
}

static partial class ProjectionProgressTrackerLogMessages {
    [LoggerMessage(LogLevel.Debug, "{name} records committed in {duration:N1} ms")]
    public static partial void LogProgressRecordsCommitted(this ILogger logger, string name, double duration);
}
