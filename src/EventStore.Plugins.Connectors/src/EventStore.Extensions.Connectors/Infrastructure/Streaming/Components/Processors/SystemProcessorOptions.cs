// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.Configuration;

namespace EventStore.Streaming.Processors.Configuration;

[PublicAPI]
public record SystemProcessorOptions : ProcessorOptions {
    public IPublisher                  Publisher       { get; init; }
    public ConsumeFilter               Filter          { get; init; } = ConsumeFilter.ExcludeSystemEvents();
    public SubscriptionInitialPosition InitialPosition { get; init; } = SubscriptionInitialPosition.Latest;
    public RecordPosition              StartPosition   { get; init; } = RecordPosition.Unset;
    public LogPosition                 LogPosition     { get; init; } = LogPosition.Unset;
    public AutoCommitOptions           AutoCommit      { get; init; } = new();
    public bool                        SkipDecoding    { get; init; }
}