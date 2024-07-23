// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Producers.Configuration;
using EventStore.Streaming.Resilience;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventStore.Connect.Producers.Configuration;

[PublicAPI]
public record SystemProducerBuilder : ProducerBuilder<SystemProducerBuilder, SystemProducerOptions, SystemProducer> {
	public SystemProducerBuilder Publisher(IPublisher publisher) {
		Ensure.NotNull(publisher);
		return new() {
			Options = Options with {
				Publisher = publisher
			}
		};
	}

	public override SystemProducer Create() {
		Ensure.NotNullOrWhiteSpace(Options.ProducerId);
		Ensure.NotNull(Options.Publisher);

		var options = Options with {
			ResiliencePipelineBuilder = Options.ResiliencePipelineBuilder.ConfigureTelemetry(
				Options.Logging.Enabled
					? Options.Logging.LoggerFactory
					: NullLoggerFactory.Instance,
				"ProducerResiliencePipelineTelemetryLogger"
			)
		};

		return new(options);
	}
}