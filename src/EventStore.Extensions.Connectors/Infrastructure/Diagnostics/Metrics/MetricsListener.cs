using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EventStore.Connectors.Diagnostics.Metrics;

sealed class MetricsListener<T>(string name, Histogram<double> duration, Counter<long> errors, Func<T, TagList> getTags)
	: GenericListener(name), IDisposable {
	protected override void OnEvent(KeyValuePair<string, object?> data) {
		if (data.Key != Measure.EventName || data.Value is not MeasureContext { Context: T context } ctx) return;

		var tags = getTags(context);

		duration.Record(ctx.Duration.TotalMilliseconds, tags);
		if (ctx.Error) errors.Add(1, tags);
	}
}