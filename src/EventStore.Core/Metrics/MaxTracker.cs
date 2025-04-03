// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using EventStore.Core.Time;

namespace EventStore.Core.Metrics;

public interface IMaxTracker<T> {
	void Record(T value);
}

// Similar to DurationMaxTracker (see notes there)
// One thread can Record while another Observes concurrently.
public class MaxTracker<T> : IMaxTracker<T> where T : struct {
	private readonly IClock _clock;
	private readonly KeyValuePair<string, object>[] _maxTags;
	private readonly RecentMax<T> _recentMax;

	public MaxTracker(
		MaxMetric<T> metric,
		string name,
		int expectedScrapeIntervalSeconds,
		IClock clock = null) {

		_clock = clock ?? Clock.Instance;

		_recentMax = new RecentMax<T>(expectedScrapeIntervalSeconds);

		var maxTags = new List<KeyValuePair<string, object>>();
		if (!string.IsNullOrWhiteSpace(name))
			maxTags.Add(new("name", name));
		maxTags.Add(new("range", $"{_recentMax.MinPeriodSeconds}-{_recentMax.MaxPeriodSeconds} seconds"));
		_maxTags = maxTags.ToArray();

		metric.Add(this);
	}

	public void Record(T value) {
		_recentMax.Record(_clock.Now, value);
	}

	public Measurement<T> Observe() {
		var value = _recentMax.Observe(_clock.Now);
		return new(value, _maxTags.AsSpan());
	}

	public class NoOp : IMaxTracker<T> {
		public void Record(T value) { }
	}
}
