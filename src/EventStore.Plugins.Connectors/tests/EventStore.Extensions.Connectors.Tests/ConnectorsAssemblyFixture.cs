using EventStore.Core.Bus;
using EventStore.Extensions.Connectors.Tests;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Consumers.Configuration;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Producers.Configuration;
using EventStore.Streaming.Readers;
using EventStore.Streaming.Readers.Configuration;
using EventStore.Streaming.Schema;
using EventStore.Streaming.Schema.Serializers;
using EventStore.Streaming.Schema.Serializers.Bytes;
using EventStore.Streaming.Schema.Serializers.Json;
using EventStore.Streaming.Schema.Serializers.Protobuf;
using EventStore.Testing;
using EventStore.Testing.Fixtures;
using EventStore.Testing.Xunit.Extensions.AssemblyFixture;
using NodaTime.Serialization.SystemTextJson;

[assembly: TestFramework(XunitTestFrameworkWithAssemblyFixture.TypeName, XunitTestFrameworkWithAssemblyFixture.AssemblyName)]
[assembly: AssemblyFixture(typeof(ConnectorsAssemblyFixture))]

namespace EventStore.Extensions.Connectors.Tests;

[PublicAPI]
public partial class ConnectorsAssemblyFixture : ClusterVNodeFixture {
    public ConnectorsAssemblyFixture() {
        SchemaRegistry = new SchemaRegistry(new InMemorySchemaRegistryClient())
            .UseBytes()
            .UseJson(new SystemJsonSerializerOptions(new() {
                Converters = { NodaConverters.InstantConverter, NodaConverters.DurationConverter }
            }))
            .UseProtobuf();

        SchemaSerializer = SchemaRegistry;

        OnSetup = () => {
            Producer = SystemProducer.Builder
                .Publisher(Publisher)
                .ProducerId("test-producer")
                .SchemaRegistry(SchemaRegistry)
                .LoggerFactory(LoggerFactory)
                .EnableLogging()
                .Create();

            Reader = SystemReader.Builder
                .Publisher(Publisher)
                .ReaderId("test-reader")
                .SchemaRegistry(SchemaRegistry)
                .LoggerFactory(LoggerFactory)
                .EnableLogging()
                .Create();

            return Task.CompletedTask;
        };

        OnTearDown = async () => {
            await Producer.DisposeAsync();
            await Reader.DisposeAsync();
        };

    }

    public SchemaRegistry    SchemaRegistry   { get; }
    public ISchemaSerializer SchemaSerializer { get; }
    public IProducer         Producer         { get; private set; } = null!;
    public IReader           Reader           { get; private set; } = null!;


    public SystemProducerBuilder NewProducer() => SystemProducer.Builder
        .Publisher(Publisher)
        .LoggerFactory(LoggerFactory)
        .SchemaRegistry(SchemaRegistry);

    public SystemReaderBuilder NewReader() => SystemReader.Builder
        .Publisher(Publisher)
        .LoggerFactory(LoggerFactory)
        .SchemaRegistry(SchemaRegistry);

    public SystemConsumerBuilder NewConsumer() => SystemConsumer.Builder
        .Publisher(Publisher)
        .LoggerFactory(LoggerFactory)
        .SchemaRegistry(SchemaRegistry);
}

public abstract class ConnectorsIntegrationTests<TFixture> where TFixture : ConnectorsAssemblyFixture {
    protected ConnectorsIntegrationTests(ITestOutputHelper output, TFixture fixture) => Fixture = fixture.With(x => x.CaptureTestRun(output));

    protected TFixture Fixture { get; }

    public IPublisher  Publisher  => Fixture.Publisher;
    public ISubscriber Subscriber => Fixture.Subscriber;
}

public abstract class ConnectorsIntegrationTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture)
    : ConnectorsIntegrationTests<ConnectorsAssemblyFixture>(output, fixture);