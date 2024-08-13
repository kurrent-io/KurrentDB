using EventStore.Connect.Connectors;
using EventStore.Streaming.Connectors.Sinks;
using EventStore.Streaming.Processors;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Testing;

public class LoggerSink : ISink {
    public void Open(SinkContext sinkContext) { }

    public Task Write(RecordContext recordContext) {
        recordContext.Logger.LogWarning("RECORD WRITTEN: {Record}", recordContext.Record);
        return Task.CompletedTask;
    }

    public ValueTask Close() => ValueTask.CompletedTask;
}

public class LoggerSinkValidator : ConnectorValidator<SinkOptions>;