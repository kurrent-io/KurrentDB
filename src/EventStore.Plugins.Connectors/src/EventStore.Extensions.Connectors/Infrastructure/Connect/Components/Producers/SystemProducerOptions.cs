// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Streaming.Configuration;
using EventStore.Streaming.Producers.Configuration;
using EventStore.Streaming.Resilience;

namespace EventStore.Connect.Producers.Configuration;

public record SystemProducerOptions : ProducerOptions {
    public SystemProducerOptions() {
        Logging = new LoggingOptions {
            LogName = "EventStore.Connect.SystemProducer"
        };

        ResiliencePipelineBuilder = DefaultRetryPolicies.ExponentialBackoffPipelineBuilder();
    }

    public IPublisher Publisher { get; init; }
}