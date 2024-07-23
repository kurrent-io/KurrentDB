using EventStore.Streaming.Connectors.Sinks;
using EventStore.Streaming.Processors;
using Microsoft.Extensions.Logging;

namespace EventStore.Connect.Connectors;

public class LoggerSink : ISink {
    public void Open(SinkContext sinkContext) { }

    public Task Write(RecordContext recordContext) {
        recordContext.Logger.LogInformation("[LoggerSink] record logged: {Record}", recordContext.Record);
        return Task.CompletedTask;
    }

    public ValueTask Close() => ValueTask.CompletedTask;
}

public class LoggerSinkValidator() : ConnectorValidator<SinkOptions>;