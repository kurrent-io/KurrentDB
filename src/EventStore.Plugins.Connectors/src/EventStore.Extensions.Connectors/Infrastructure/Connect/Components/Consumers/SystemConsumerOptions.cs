// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using Kurrent.Surge.Configuration;
using Kurrent.Surge.Consumers;
using Kurrent.Surge.Consumers.Configuration;

namespace EventStore.Connect.Consumers.Configuration;

public record SystemConsumerOptions : ConsumerOptions {
    public SystemConsumerOptions() {
        Logging = new LoggingOptions {
            LogName = "EventStore.Connect.SystemConsumer"
        };

        Filter = ConsumeFilter.ExcludeSystemEvents();
    }

    public IPublisher Publisher { get; init; }
}