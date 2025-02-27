// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using Kurrent.Surge.Configuration;
using Kurrent.Surge.Producers.Configuration;
using Kurrent.Surge.Resilience;

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