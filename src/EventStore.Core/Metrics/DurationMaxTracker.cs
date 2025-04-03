// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using EventStore.Core.Time;

namespace EventStore.Core.Metrics;

public interface IDurationMaxTracker {
	// Returns the current instant
	Instant RecordNow(Instant start);
}

// When observed, this returns the max duration that it has been asked to record over the last x
// seconds, where x is not exact, but within a known range.
// The idea is, if we expect to take an observation every e.g. ~15 seconds, and that observation
// contains the max duration over the last 16-20 seconds, then every duration will be captured
// at least once (although, some of them will be captured twice, which isn't as bad as missing them).
//
// It is useful for observing values with high volatility that may spike up significantly and then
// return to normal between scrapes and therefore the spike would otherwise be missing from the metrics.
//
// It works by recording max values in buckets, each bucket representing one sub-period.
// The max value for the period is then the max across all the buckets.
// This keeps resource consumption low even when there are a lot of durations recorded.
// On recording/observing, buckets that contain data that is old enough to not be relevant are reset.
//
// One thread can RecordNow while another Observes concurrently.
public class DurationMaxTracker : IDurationMaxTracker {
	private readonly IClock _clock;
	private readonly KeyValuePair<string, object>[] _maxTags;
	private readonly RecentMax<double> _recentMax;

	public DurationMaxTracker(
		DurationMaxMetric metric,
		string name,
		int expectedScrapeIntervalSeconds,
		IClock clock = null) {

		_clock = clock ?? Clock.Instance;
		_recentMax = new RecentMax<double>(expectedScrapeIntervalSeconds);

		var maxTags = new List<KeyValuePair<string, object>>();
		if (!string.IsNullOrWhiteSpace(name))
			maxTags.Add(new("name", name));
		maxTags.Add(new("range", $"{_recentMax.MinPeriodSeconds}-{_recentMax.MaxPeriodSeconds} seconds"));
		_maxTags = maxTags.ToArray();

		metric.Add(this);
	}

	public Instant RecordNow(Instant start) {
		var now = _clock.Now;
		var elapsedSeconds = now.ElapsedSecondsSince(start);
		_recentMax.Record(now, elapsedSeconds);
		return now;
	}

	public void RecordNow(TimeSpan duration) {
		_recentMax.Record(_clock.Now, duration.TotalSeconds);
	}

	public Measurement<double> Observe() {
		var value = _recentMax.Observe(_clock.Now);
		return new(value, _maxTags.AsSpan());
	}

	public class NoOp : IDurationMaxTracker {
		public Instant RecordNow(Instant start) => Instant.Now;
	}
}
