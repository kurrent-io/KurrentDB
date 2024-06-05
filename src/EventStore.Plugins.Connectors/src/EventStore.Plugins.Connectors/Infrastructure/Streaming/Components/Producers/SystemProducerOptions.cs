// ReSharper disable CheckNamespace

using EventStore.Core.Bus;

namespace EventStore.Streaming.Producers.Configuration;

public record SystemProducerOptions : ProducerOptions {
	public SystemProducerOptions() {
		ProducerName  = Identifiers.GenerateShortId("pdr");
	}

	public IPublisher Publisher { get; init; }
}