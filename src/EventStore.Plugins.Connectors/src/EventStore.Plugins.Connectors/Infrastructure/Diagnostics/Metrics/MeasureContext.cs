namespace EventStore.Connectors.Infrastructure.Diagnostics.Metrics;

record MeasureContext(TimeSpan Duration, bool Error, object Context);