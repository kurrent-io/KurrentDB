// ReSharper disable CheckNamespace

using EventStore.Core.Bus;

namespace EventStore.Streaming.Consumers.Configuration;

public record SystemConsumerOptions : ConsumerOptions {
	public SystemConsumerOptions() {
		var consumerName = Identifiers.GenerateShortId("csr");

		ConsumerName              = consumerName;
		SubscriptionName          = consumerName;
		Filter                    = ConsumeFilter.ExcludeSystemEvents();
		Publisher                 = null!;
	}

	public IPublisher Publisher { get; init; }
}