using System.Diagnostics;

namespace EventStore.Connectors.Diagnostics.Metrics;

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