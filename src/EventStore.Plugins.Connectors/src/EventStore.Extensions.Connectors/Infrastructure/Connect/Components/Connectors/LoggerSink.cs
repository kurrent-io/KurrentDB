using EventStore.Streaming;
using EventStore.Streaming.Connectors.Sinks;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Testing;

public class LoggerSink : ISink {
    LogLevel LogLevel { get; set; }

    public ValueTask Open(SinkOpenContext context) {
        var options = context.Configuration.GetRequiredOptions<LoggerSinkOptions>();
        LogLevel = options.LogLevel;
        return ValueTask.CompletedTask;
    }

    public ValueTask Write(SinkWriteContext context) {
        context.Logger.LogRecordWritten(LogLevel, context.ConnectorId, context.Record);
        return ValueTask.CompletedTask;
    }
}

static partial class LoggerSinkLogMessages {
    [LoggerMessage("{ConnectorId} RECORD WRITTEN: {Record}")]
    internal static partial void LogRecordWritten(this ILogger logger, LogLevel level, string connectorId, EventStoreRecord record);
}

[PublicAPI]
public record LoggerSinkOptions : SinkOptions {
    public LogLevel LogLevel { get; init; } = LogLevel.Debug;
}

[PublicAPI]
public class LoggerSinkValidator : SinkConnectorValidator<SinkOptions>;