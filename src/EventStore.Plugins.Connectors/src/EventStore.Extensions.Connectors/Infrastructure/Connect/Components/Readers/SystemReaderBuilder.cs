// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Streaming;
using EventStore.Streaming.Readers.Configuration;
using EventStore.Streaming.Resilience;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventStore.Connect.Readers.Configuration;

[PublicAPI]
public record SystemReaderBuilder : ReaderBuilder<SystemReaderBuilder, SystemReaderOptions,SystemReader> {
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

        var options = Options with {
            ResiliencePipelineBuilder = Options.ResiliencePipelineBuilder.ConfigureTelemetry(
                Options.EnableLogging
                    ? Options.LoggerFactory
                    : NullLoggerFactory.Instance,
                "ReaderResilienceTelemetryLogger"
            )
        };

		return new(options);
	}
}