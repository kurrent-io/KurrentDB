// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Streaming.Resilience;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventStore.Streaming.Producers.Configuration;

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
				Options.EnableLogging
					? Options.LoggerFactory
					: NullLoggerFactory.Instance,
				"ProducerResilienceTelemetryLogger"
			)
		};

		return new(options);
	}
}