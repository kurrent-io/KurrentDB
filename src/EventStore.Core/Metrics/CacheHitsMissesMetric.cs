// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using static EventStore.Common.Configuration.MetricsConfiguration;

namespace EventStore.Core.Metrics;

public class CacheHitsMissesMetric {
	private readonly Dictionary<Cache, string> _enabledCaches;
	private readonly List<Func<long>> _funcs = new();
	private readonly List<KeyValuePair<string, object>[]> _tagss = new();
	private readonly object _lock = new();

	public CacheHitsMissesMetric(
		Meter meter,
		Cache[] enabledCaches,
		string name,
		Dictionary<Cache, string> cacheNames) {

		_enabledCaches = new(cacheNames.Where(x => enabledCaches.Contains(x.Key)));
		meter.CreateObservableCounter(name, Observe);
	}

	public void Register(Cache cache, Func<long> getHits, Func<long> getMisses) {
		Register(cache, "hits", getHits);
		Register(cache, "misses", getMisses);
	}

	private void Register(Cache cache, string kind, Func<long> func) {
		if (!_enabledCaches.TryGetValue(cache, out var cacheName))
			return;

		lock (_lock) {
			_funcs.Add(func);
			_tagss.Add(new KeyValuePair<string, object>[] {
				new("cache", cacheName),
				new("kind", kind),
			});
		}
	}

	private IEnumerable<Measurement<long>> Observe() {
		lock (_lock) {
			for (var i = 0; i < _funcs.Count; i++) {
				yield return new(_funcs[i](), _tagss[i]);
			}
		}
	}
}
