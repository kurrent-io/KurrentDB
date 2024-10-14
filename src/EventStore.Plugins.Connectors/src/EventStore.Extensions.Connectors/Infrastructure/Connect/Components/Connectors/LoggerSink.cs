using EventStore.Connect.Connectors;
using EventStore.Streaming.Connectors.Sinks;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Testing;

public class LoggerSink : ISink {
    public ValueTask Open(SinkOpenContext context) => ValueTask.CompletedTask;

    public ValueTask Write(SinkWriteContext context) {
        context.Logger.LogWarning("RECORD WRITTEN: {Record}", context.Record);
        return ValueTask.CompletedTask;
    }
}

public class LoggerSinkValidator : ConnectorValidator<SinkOptions>;