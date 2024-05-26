// ReSharper disable CheckNamespace

using EventStore.Core.Bus;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Producers;

namespace EventStore.Streaming.Processors.Configuration;

[PublicAPI]
public record SystemProcessorOptions : ProcessorOptions<SystemProcessorOptions> {
	public SystemProcessorOptions() {
		GetConsumer = options => SystemConsumer.Builder
			.ConsumerName($"{options.ProcessorId}-csr")
			.SubscriptionName(options.SubscriptionName)
			.Streams(options.Streams)
			.InitialPosition(options.InitialPosition)
			.StartPosition(options.StartPosition)
			.MessagePrefetchCount(options.MessagePrefetchCount)
			.Filter(options.Filter)
			.AutoCommit(options.AutoCommit)
			.SkipDecoding(options.SkipDecoding)
			.LoggerFactory(options.LoggerFactory)
			.SchemaRegistry(options.SchemaRegistry)
			.Interceptors(options.Interceptors)
			.Publisher(options.Publisher)
			.Create();

		GetProducer = options => SystemProducer.Builder
			.ProducerName($"{options.ProcessorId}-pdr")
			.DefaultStream(options.DefaultOutputStream)
			.LoggerFactory(options.LoggerFactory)
			.SchemaRegistry(options.SchemaRegistry)
			.Interceptors(options.Interceptors)
			.Publisher(options.Publisher)
			.Create();
	}

	public IPublisher Publisher { get; init; }
}