// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Resilience;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventStore.Connect.Consumers.Configuration;

[PublicAPI]
public record SystemConsumerBuilder : ConsumerBuilder<SystemConsumerBuilder, SystemConsumerOptions, SystemConsumer> {
    public SystemConsumerBuilder Publisher(IPublisher publisher) {
        Ensure.NotNull(publisher);
        return new() {
            Options = Options with {
                Publisher = publisher
            }
        };
    }

    public override SystemConsumer Create() {
        Ensure.NotNullOrWhiteSpace(Options.ConsumerId);
        Ensure.NotNullOrWhiteSpace(Options.SubscriptionName);
        Ensure.NotNull(Options.Publisher);

        var options = Options with {
            ResiliencePipelineBuilder = Options.ResiliencePipelineBuilder.ConfigureTelemetry(
                Options.EnableLogging
                    ? Options.LoggerFactory
                    : NullLoggerFactory.Instance,
                "ConsumerResilienceTelemetryLogger"
            )
        };

        return new(options);
    }
}