// ReSharper disable CheckNamespace

using EventStore.Core.Bus;

namespace EventStore.Streaming.Producers.Configuration;

public record SystemProducerOptions : ProducerOptions {
	public IPublisher Publisher { get; init; }
}