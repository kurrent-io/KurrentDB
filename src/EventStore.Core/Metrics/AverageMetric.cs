// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Tag = System.Collections.Generic.KeyValuePair<string, object>;

namespace EventStore.Core.Metrics;

// When observed, this calculates the average for each group
public class AverageMetric {
	private readonly object _lock = new();
	private readonly Func<string, Tag> _genTag;
	private readonly Dictionary<string, (List<Func<double>>, Tag[])> _subMetricGroups = new();

	public AverageMetric(Meter meter, string name, string unit, Func<string, Tag> genTag, bool legacyNames) {
		_genTag = genTag;
		if (legacyNames)
			meter.CreateObservableCounter(name + "-" + unit, Observe);
		else
			meter.CreateObservableCounter(name, Observe, unit);
	}

	public void Register(string group, Func<double> subMetric) {
		lock (_lock) {
			if (_subMetricGroups.TryGetValue(group, out var pair)) {
				pair.Item1.Add(subMetric);
			} else {
				var tags = new[] { _genTag(group) };
				_subMetricGroups[group] = (new() { subMetric }, tags);
			}
		}
	}

	private IEnumerable<Measurement<double>> Observe() {
		lock (_lock) {
			foreach (var (groupFuncs, groupTags) in _subMetricGroups.Values) {
				var total = 0d;
				foreach (var observe in groupFuncs) {
					total += observe();
				}
				var average = total / groupFuncs.Count;
				yield return new(average, groupTags);
			}
		}
	}
}
