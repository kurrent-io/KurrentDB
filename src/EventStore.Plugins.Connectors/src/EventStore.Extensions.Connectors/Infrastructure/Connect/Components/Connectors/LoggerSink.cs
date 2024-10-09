using EventStore.Connect.Connectors;
using EventStore.Streaming.Connectors.Sinks;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Testing;

public class LoggerSink : ISink {
    public ValueTask Open(SinkOpenContext sinkContext) => ValueTask.CompletedTask;

    public ValueTask Write(SinkWriteContext recordContext) {
        recordContext.Logger.LogWarning("RECORD WRITTEN: {Record}", recordContext.Record);
        return ValueTask.CompletedTask;
    }

    public ValueTask Close() => ValueTask.CompletedTask;
}

public class LoggerSinkValidator : ConnectorValidator<SinkOptions>;