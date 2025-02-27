// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using Kurrent.Surge.Readers.Configuration;
using Kurrent.Toolkit;

namespace EventStore.Connect.Readers.Configuration;

[PublicAPI]
public record SystemReaderBuilder : ReaderBuilder<SystemReaderBuilder, SystemReaderOptions> {
    public SystemReaderBuilder Publisher(IPublisher publisher) {
        Ensure.NotNull(publisher);
        return new() {
            Options = Options with {
                Publisher = publisher
            }
        };
    }

	public override SystemReader Create() {
        Ensure.NotNullOrWhiteSpace(Options.ReaderId);
        Ensure.NotNull(Options.Publisher);

        return new(Options with {});

        // var options = Options with {
        //     ResiliencePipelineBuilder = Options.ResiliencePipelineBuilder.ConfigureTelemetry(Options.Logging.Enabled
        //             ? Options.Logging.LoggerFactory
        //             : NullLoggerFactory.Instance,
        //         "ReaderResiliencePipelineTelemetryLogger")
        // };

		// return new(options);
	}
}