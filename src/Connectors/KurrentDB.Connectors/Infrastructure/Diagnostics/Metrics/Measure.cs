// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;

namespace KurrentDB.Connectors.Infrastructure.Diagnostics.Metrics;

sealed class Measure(DiagnosticSource diagnosticSource, object context) : IDisposable {
	public static Measure Start(DiagnosticSource source, object context) => new(source, context);

	public const string EventName = "Stopped";

	public void SetError() => _error = true;

	readonly long _startedAt = TimeProvider.System.GetTimestamp();

	bool _error;

	void Record() {
		var duration = TimeProvider.System.GetElapsedTime(_startedAt);
		diagnosticSource.Write(EventName, new MeasureContext(duration, _error, context));
	}

	public void Dispose() => Record();
}