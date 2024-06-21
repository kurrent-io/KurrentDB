// ReSharper disable CheckNamespace

using EventStore.Core.Bus;

namespace EventStore.Streaming.Consumers.Configuration;

public record SystemConsumerOptions : ConsumerOptions {
    public SystemConsumerOptions() =>
        Filter = ConsumeFilter.ExcludeSystemEvents();

    public IPublisher Publisher { get; init; }
}