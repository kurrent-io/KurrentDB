// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;

namespace EventStore.Core.Metrics;

internal class Dimensions<TTracker, TData> where TTracker : notnull where TData : struct {
	private readonly List<Func<Measurement<TData>>> _funcs = new();
	private readonly Func<string, KeyValuePair<string, object>> _genTag;
	private readonly Dictionary<TTracker, string> _enabledDimensions;

	public Dimensions(
		Dictionary<TTracker, bool> config,
		Dictionary<TTracker, string> dimNames,
		Func<string, KeyValuePair<string, object>> genTag) {

		_enabledDimensions = dimNames
			.Where(kvp => config.TryGetValue(kvp.Key, out var enabled) && enabled)
			.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
		_genTag = genTag;
	}

	public bool AnyRegistered() => _funcs.Any();

	public void Register(TTracker tracker, Func<TData> func) {
		if (!_enabledDimensions.TryGetValue(tracker, out var dimension))
			return;

		var tags = new[] { _genTag(dimension) };
		_funcs.Add(() => new(func(), tags.AsSpan()));
	}

	public void Register(TTracker tracker, Func<string, Measurement<TData>> func) {
		if (!_enabledDimensions.TryGetValue(tracker, out var dimension))
			return;

		_funcs.Add(() => func(dimension));
	}

	public Func<IEnumerable<Measurement<TData>>> GenObserve() {
		var measurements = new Measurement<TData>[_funcs.Count];

		return () => {
			for (var i = 0; i < measurements.Length; i++) {
				measurements[i] = _funcs[i]();
			}
			return measurements;
		};
	}
}
