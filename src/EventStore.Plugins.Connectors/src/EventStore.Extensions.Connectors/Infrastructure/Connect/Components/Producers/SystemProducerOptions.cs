// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Streaming.Producers.Configuration;

namespace EventStore.Connect.Producers.Configuration;

public record SystemProducerOptions : ProducerOptions {
	public IPublisher Publisher { get; init; }
}