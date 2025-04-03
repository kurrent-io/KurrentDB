// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace EventStore.Core.XUnit.Tests.Metrics;

public class TestMeterListener<T> : IDisposable where T : struct {
	private readonly MeterListener _listener;
	private readonly Dictionary<string, List<TestMeasurement>> _measurementsByInstrument;

	public TestMeterListener(Meter meter) {
		_measurementsByInstrument = new();
		_listener = new MeterListener {
			InstrumentPublished = (instrument, listener) => {
				if (instrument.Meter == meter) {
					listener.EnableMeasurementEvents(instrument);
				}
			}
		};
		_listener.SetMeasurementEventCallback<T>(OnMeasurement);
		_listener.Start();
	}

	public void Dispose() {
		_listener?.Dispose();
	}

	public void Observe() {
		_listener.RecordObservableInstruments();
	}

	// gets the measurements for a given instrument and clears them
	public IReadOnlyList<TestMeasurement> RetrieveMeasurements(string instrumentName) {
		if (!_measurementsByInstrument.Remove(instrumentName, out var measurements)) {
			return Array.Empty<TestMeasurement>();
		}

		return measurements;
	}

	private void OnMeasurement(
		Instrument instrument,
		T value,
		ReadOnlySpan<KeyValuePair<string, object>> tags,
		object state) {

		var instrumentName = GenName(instrument);
		if (!_measurementsByInstrument.TryGetValue(instrumentName, out var measurements)) {
			measurements = new();
			_measurementsByInstrument[instrumentName] = measurements;
		}

		measurements.Add(new TestMeasurement {
			Value = value,
			Tags = tags.ToArray(),
		});
	}

	private static string GenName(Instrument instrument) =>
		string.IsNullOrWhiteSpace(instrument.Unit)
		? instrument.Name
		: instrument.Name + "-" + instrument.Unit;

	public class TestMeasurement {
		public T Value { get; init; }
		public KeyValuePair<string, object>[] Tags { get; init; }
	}
}
