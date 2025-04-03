// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;

namespace KurrentDB.Tools;

public sealed class MetricsObserver : IDisposable {
	readonly MeterListener _meterListener = new();

	public MetricsObserver() {
		_meterListener.InstrumentPublished = (instrument, listener) => {
			if (instrument.Name.StartsWith("kestrel")) {
				listener.EnableMeasurementEvents(instrument);
			}
		};
		_meterListener.SetMeasurementEventCallback<long>(OnMeasurementRecorded);
		_meterListener.Start();
	}

	public event EventHandler<EventArgs> DataUpdated;

	void OnMeasurementRecorded<T>(
		Instrument instrument,
		T measurement,
		ReadOnlySpan<KeyValuePair<string, object>> tags,
		object state) {
		if (measurement is not long value) return;
		if (instrument.Name == "kestrel.active_connections") {
			Interlocked.Add(ref _nrActiveConnections, value);
			DataUpdated?.Invoke(null, EventArgs.Empty);
		}
	}

	public long ActiveConnections => _nrActiveConnections;

	long _nrActiveConnections;

	public void Dispose() {
		_meterListener?.Dispose();
	}
}
