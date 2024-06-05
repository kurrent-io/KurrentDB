// ReSharper disable CheckNamespace

using EventStore.Core.Bus;

namespace EventStore.Streaming.Processors.Configuration;

[PublicAPI]
public record SystemProcessorBuilder : ProcessorBuilder<SystemProcessorBuilder, SystemProcessorOptions> {
	public SystemProcessorBuilder Publisher(IPublisher publisher) {
		Ensure.NotNull(publisher);
		return new() {
			Options = Options with {
				Publisher = publisher
			}
		};
	}

	public override SystemProcessor Create() {
		Ensure.NotNullOrWhiteSpace(Options.ProcessorName);
		Ensure.NotNullOrWhiteSpace(Options.SubscriptionName);
        Ensure.NotNullOrEmpty(Options.RouterRegistry.Endpoints);
		Ensure.NotNull(Options.Publisher);

		var options = Options with { };

		return new(options);
	}
}