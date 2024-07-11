// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.Configuration;

namespace EventStore.Connect.Consumers.Configuration;

public record SystemConsumerOptions : ConsumerOptions {
    public SystemConsumerOptions() =>
        Filter = ConsumeFilter.ExcludeSystemEvents();

    public IPublisher Publisher { get; init; }
}