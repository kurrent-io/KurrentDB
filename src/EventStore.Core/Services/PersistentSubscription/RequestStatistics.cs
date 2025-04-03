// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace EventStore.Core.Services.PersistentSubscription;

public class RequestStatistics {
	private readonly Func<long> _getElapsedTicks;

	//TODO CC this can likely be done in a smarter way (though a few thousand ints is still pretty cheap memory wise)
	private readonly Queue<long> _measurements;
	private readonly ConcurrentDictionary<Guid, Operation> _operations = new();
	private readonly int _windowSize;

	public RequestStatistics(Func<long> getElapsedTicks, int windowSize) {
		_getElapsedTicks = getElapsedTicks;
		_windowSize = windowSize;
		_measurements = new Queue<long>(windowSize);
	}

	public RequestStatistics(Stopwatch watch, int windowSize) : this(() => watch.ElapsedTicks, windowSize) {
	}

	public void StartOperation(Guid id) {
		var record = new Operation {Start = _getElapsedTicks()};
		_operations.AddOrUpdate(id, record, (_, _) => record);
	}

	public void EndOperation(Guid id) {
		if (!_operations.TryRemove(id, out var record)) return;
		var current = _getElapsedTicks();
		var time = current - record.Start;
		var ms = time / TimeSpan.TicksPerMillisecond;
		if (_measurements.Count >= _windowSize) {
			_measurements.Dequeue();
		}

		_measurements.Enqueue(ms);
	}

	public ObservedTimingMeasurement GetMeasurementDetails() {
		var ret = new ObservedTimingMeasurement();
		if (_measurements == null || _measurements.Count == 0) return ret;
		var items = _measurements.ToArray();
		Array.Sort(items);
		ret.Measurements.Add(Measurement.From("Mean", items.Sum() / items.Length));
		ret.Measurements.Add(Measurement.From("Median", items[items.Length / 2]));
		ret.Measurements.Add(Measurement.From("Fastest", items[0]));
		for (var i = 0; i < 5; i++) {
			ret.Measurements.Add(
				Measurement.From("Quintile " + (i + 1), items[GetPercentile(i * 20, items.Length)]));
		}

		ret.Measurements.Add(Measurement.From("90%", items[GetPercentile(90m, items.Length)]));
		ret.Measurements.Add(Measurement.From("95%", items[GetPercentile(95m, items.Length)]));
		ret.Measurements.Add(Measurement.From("99%", items[GetPercentile(99m, items.Length)]));
		ret.Measurements.Add(Measurement.From("99.5%", items[GetPercentile(99.5m, items.Length)]));
		ret.Measurements.Add(Measurement.From("99.9%", items[GetPercentile(99.9m, items.Length)]));
		ret.Measurements.Add(Measurement.From("Highest", items[^1]));
		return ret;
	}

	public void ClearMeasurements() {
		_measurements.Clear();
	}

	private int GetPercentile(decimal percentile, int size) {
		decimal percent = 0;
		percent = percentile / 100m;
		var ret = (int)(percent * size);
		if (ret == size) ret -= 1;
		return ret;
	}

	struct Operation {
		public long Start;
	}
}

public class ObservedTimingMeasurement {
	public readonly List<Measurement> Measurements = new List<Measurement>();
}

public struct Measurement {
	public readonly string Key;
	public readonly long Value;

	public Measurement(string key, long value) {
		Key = key;
		Value = value;
	}

	public static Measurement From(string key, long value) => new(key, value);
}
