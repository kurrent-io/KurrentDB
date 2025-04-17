// ReSharper disable CheckNamespace

using KurrentDB.Core.Bus;
using Kurrent.Surge.Configuration;
using Kurrent.Surge.Producers.Configuration;
using Kurrent.Surge.Resilience;

namespace EventStore.Connect.Producers.Configuration;

public record SystemProducerOptions : ProducerOptions {
    public SystemProducerOptions() {
        Logging = new LoggingOptions {
            LogName = "Kurrent.Surge.SystemProducer"
        };

        ResiliencePipelineBuilder = DefaultRetryPolicies.ExponentialBackoffPipelineBuilder();
    }

    public IPublisher Publisher { get; init; }
}
