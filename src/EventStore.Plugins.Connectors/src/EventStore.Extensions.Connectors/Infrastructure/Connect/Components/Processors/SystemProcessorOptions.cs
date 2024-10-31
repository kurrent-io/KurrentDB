// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Configuration;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Processors.Configuration;

namespace EventStore.Connect.Processors.Configuration;

[PublicAPI]
public record SystemProcessorOptions : ProcessorOptions {
    public SystemProcessorOptions() {
        Logging = new LoggingOptions {
            LogName = "EventStore.Connect.SystemProcessor"
        };
    }

    public IPublisher    Publisher { get; init; }
    public ConsumeFilter Filter    { get; init; } = ConsumeFilter.ExcludeSystemEvents();

    public RecordPosition?             StartPosition   { get; init; } = RecordPosition.Unset;
    public SubscriptionInitialPosition InitialPosition { get; init; } = SubscriptionInitialPosition.Latest;

    public AutoCommitOptions AutoCommit   { get; init; } = new();
    public bool              SkipDecoding { get; init; }
}