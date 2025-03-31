namespace EventStore.Connectors.Diagnostics.Metrics;

record MeasureContext(TimeSpan Duration, bool Error, object Context);