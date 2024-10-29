using System.Runtime.CompilerServices;
using EventStore.Connect;
using EventStore.Connect.Consumers;
using EventStore.Connect.Consumers.Configuration;
using EventStore.Connect.Processors;
using EventStore.Connect.Processors.Configuration;
using EventStore.Connect.Producers;
using EventStore.Connect.Producers.Configuration;
using EventStore.Connect.Readers;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connectors.Eventuous;
using EventStore.Connectors.Management;
using EventStore.Connectors.Management.Queries;
using EventStore.Connectors.System;
using EventStore.Extensions.Connectors.Tests;
using EventStore.Plugins.Licensing;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Readers;
using EventStore.Streaming.Schema;
using EventStore.Streaming.Schema.Serializers;
using EventStore.System.Testing.Fixtures;
using EventStore.Toolkit.Testing;
using EventStore.Toolkit.Testing.Xunit.Extensions.AssemblyFixture;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

[assembly: TestFramework(XunitTestFrameworkWithAssemblyFixture.TypeName, XunitTestFrameworkWithAssemblyFixture.AssemblyName)]
[assembly: AssemblyFixture(typeof(ConnectorsAssemblyFixture))]

namespace EventStore.Extensions.Connectors.Tests;

[PublicAPI]
public partial class ConnectorsAssemblyFixture : ClusterVNodeFixture {
    public ConnectorsAssemblyFixture() {
        SchemaRegistry   = SchemaRegistry.Global;
        SchemaSerializer = SchemaRegistry;

        ConfigureServices = services => {
            services.AddNodeSystemInfoProvider();
            // services.AddConnectSystemComponents();


            // // Management
            // services.AddSingleton(ctx => new ConnectorsLicenseService(
            //     ctx.GetRequiredService<ILicenseService>(),
            //     ctx.GetRequiredService<ILogger<ConnectorsLicenseService>>()
            // ));
            //
            // services.AddConnectorsManagementSchemaRegistration();
            //
            // services
            //     .AddEventStore<SystemEventStore>(ctx => {
            //         var reader = ctx.GetRequiredService<Func<SystemReaderBuilder>>()()
            //             .ReaderId("rdx-eventuous-eventstore")
            //             .Create();
            //
            //         var producer = ctx.GetRequiredService<Func<SystemProducerBuilder>>()()
            //             .ProducerId("pdx-eventuous-eventstore")
            //             .Create();
            //
            //         return new SystemEventStore(reader, producer);
            //     })
            //     .AddCommandService<ConnectorsCommandApplication, ConnectorEntity>();
            //
            // // Queries
            // services.AddSingleton<ConnectorQueries>(ctx => new ConnectorQueries(
            //     ctx.GetRequiredService<Func<SystemReaderBuilder>>(),
            //     ConnectorQueryConventions.Streams.ConnectorsStateProjectionStream)
            // );
        };

        OnSetup = () => {
            Producer = NewProducer()
                .ProducerId("test-pdx")
                .Create();

            Reader = NewReader()
                .ReaderId("test-rdx")
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

    public IProducer Producer { get; private set; } = null!;
    public IReader   Reader   { get; private set; } = null!;

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

    public SystemProcessorBuilder NewProcessor() => SystemProcessor.Builder
        .Publisher(Publisher)
        .LoggerFactory(LoggerFactory)
        .SchemaRegistry(SchemaRegistry);

    public string NewIdentifier([CallerMemberName] string? name = null) =>
        $"{name.Underscore()}-{GenerateShortId()}".ToLowerInvariant();
}

public abstract class ConnectorsIntegrationTests<TFixture> where TFixture : ConnectorsAssemblyFixture {
    protected ConnectorsIntegrationTests(ITestOutputHelper output, TFixture fixture) => Fixture = fixture.With(x => x.CaptureTestRun(output));

    protected TFixture Fixture { get; }
}

public abstract class ConnectorsIntegrationTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture)
    : ConnectorsIntegrationTests<ConnectorsAssemblyFixture>(output, fixture);