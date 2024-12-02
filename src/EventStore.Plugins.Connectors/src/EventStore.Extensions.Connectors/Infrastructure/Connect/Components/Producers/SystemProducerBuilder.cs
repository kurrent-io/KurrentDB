// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Streaming.Producers.Configuration;
using EventStore.Toolkit;

namespace EventStore.Connect.Producers.Configuration;

[PublicAPI]
public record SystemProducerBuilder : ProducerBuilder<SystemProducerBuilder, SystemProducerOptions> {
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

        return new(Options with {});

		// var options = Options with {
		// 	ResiliencePipelineBuilder = Options.ResiliencePipelineBuilder.ConfigureTelemetry(
		// 		Options.Logging.Enabled
		// 			? Options.Logging.LoggerFactory
		// 			: NullLoggerFactory.Instance,
		// 		"ProducerResiliencePipelineTelemetryLogger"
		// 	)
		// };
		//
		// return new(Options);
	}
}