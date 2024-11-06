using System.Runtime.CompilerServices;
using EventStore.Connect.Consumers;
using EventStore.Connect.Consumers.Configuration;
using EventStore.Connect.Processors;
using EventStore.Connect.Processors.Configuration;
using EventStore.Connect.Producers;
using EventStore.Connect.Producers.Configuration;
using EventStore.Connect.Readers;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connectors.Infrastructure;
using EventStore.Connectors.Management;
using EventStore.Extensions.Connectors.Tests;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Persistence.State;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Readers;
using EventStore.Streaming.Schema;
using EventStore.Streaming.Schema.Serializers;
using EventStore.System.Testing.Fixtures;
using EventStore.Toolkit.Testing.Xunit.Extensions.AssemblyFixture;
using Microsoft.Extensions.DependencyInjection;
using FakeTimeProvider = Microsoft.Extensions.Time.Testing.FakeTimeProvider;
using WithExtension = EventStore.Toolkit.Testing.WithExtension;

[assembly: TestFramework(XunitTestFrameworkWithAssemblyFixture.TypeName, XunitTestFrameworkWithAssemblyFixture.AssemblyName)]
[assembly: AssemblyFixture(typeof(ConnectorsAssemblyFixture))]

namespace EventStore.Extensions.Connectors.Tests;

[PublicAPI]
public partial class ConnectorsAssemblyFixture : ClusterVNodeFixture {
    public ConnectorsAssemblyFixture() {
        SchemaRegistry   = SchemaRegistry.Global;
        SchemaSerializer = SchemaRegistry;
        StateStore       = new InMemoryStateStore();
        TimeProvider     = new FakeTimeProvider();

        ConfigureServices = services => {
            services
                .AddSingleton(SchemaRegistry)
                .AddSingleton(StateStore)
                .AddSingleton<TimeProvider>(TimeProvider)
                .AddSingleton(LoggerFactory);

            // System components
            services
                .AddSingleton<Func<SystemReaderBuilder>>(_ => () => NewReader().ReaderId("test-rdx"))
                .AddSingleton<Func<SystemConsumerBuilder>>(_ => () => NewConsumer().ConsumerId("test-csx"))
                .AddSingleton<Func<SystemProducerBuilder>>(_ => () => NewProducer().ProducerId("test-pdx"))
                .AddSingleton<Func<SystemProcessorBuilder>>(_ => () => NewProcessor().ProcessorId("test-pcx").StateStore(StateStore));

            // Projection store
            services.AddSingleton<ISnapshotProjectionsStore, SystemSnapshotProjectionsStore>();

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

    public SchemaRegistry    SchemaRegistry    { get; }
    public ISchemaSerializer SchemaSerializer  { get; }
    public IStateStore       StateStore        { get; private set; } = null!;
    public FakeTimeProvider  TimeProvider      { get; private set; } = null!;
    public IServiceProvider  ConnectorServices { get; private set; } = null!;

    public ISnapshotProjectionsStore SnapshotProjectionsStore => NodeServices.GetRequiredService<ISnapshotProjectionsStore>();

    public IProducer Producer { get; private set; } = null!;
    public IReader   Reader   { get; private set; } = null!;

    public ConnectorsCommandApplication CommandApplication { get; private set; } = null!;

    SequenceIdGenerator SequenceIdGenerator { get; } = new();

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

    public RecordContext CreateRecordContext(string? connectorId = null, CancellationToken cancellationToken = default) {
        connectorId ??= NewConnectorId();

        var context = new RecordContext(new ProcessorMetadata {
                ProcessorId          = connectorId,
                ClientId             = connectorId,
                SubscriptionName     = connectorId,
                Filter               = ConsumeFilter.None,
                State                = ProcessorState.Unspecified,
                Endpoints            = [],
                StartPosition        = RecordPosition.Earliest,
                LastCommitedPosition = RecordPosition.Unset
            },
            EventStoreRecord.None,
            FakeConsumer.Instance,
            StateStore,
            CreateLogger("TestLogger"),
            SchemaRegistry,
            cancellationToken);

        return context;
    }

    public async ValueTask<EventStoreRecord> CreateRecord<T>(T message, SchemaDefinitionType schemaType = SchemaDefinitionType.Json, string? streamId = null) {
        var schemaInfo = SchemaRegistry.CreateSchemaInfo<T>(schemaType);

        var data = await ((ISchemaSerializer)SchemaRegistry).Serialize(message, schemaInfo);

        var sequenceId = SequenceIdGenerator.FetchNext().Value;

        var headers = new Headers();
        schemaInfo.InjectIntoHeaders(headers);

        return new EventStoreRecord {
            Id = Guid.NewGuid(),
            Position = streamId is null
                ? RecordPosition.ForLog(sequenceId)
                : RecordPosition.ForStream(streamId, StreamRevision.From((long)sequenceId), sequenceId),
            Timestamp  = TimeProvider.GetUtcNow().UtcDateTime,
            SchemaInfo = schemaInfo,
            Data       = data,
            Value      = message!,
            ValueType  = typeof(T),
            SequenceId = sequenceId,
            Headers    = headers
        };
    }
}

public abstract class ConnectorsIntegrationTests<TFixture> where TFixture : ConnectorsAssemblyFixture {
    protected ConnectorsIntegrationTests(ITestOutputHelper output, TFixture fixture) => Fixture = WithExtension.With(fixture, x => x.CaptureTestRun(output));

    protected TFixture Fixture { get; }
}

public abstract class ConnectorsIntegrationTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture)
    : ConnectorsIntegrationTests<ConnectorsAssemblyFixture>(output, fixture);

class FakeConsumer : IConsumer {
    public static readonly IConsumer Instance = new FakeConsumer();

    public string         ConsumerId           { get; } = "";
    public string         ClientId             { get; } = "";
    public string         SubscriptionName     { get; } = "";
    public ConsumeFilter  Filter               { get; } = ConsumeFilter.None;
    public RecordPosition StartPosition        { get; } = RecordPosition.Unset;
    public RecordPosition LastCommitedPosition { get; } = RecordPosition.Unset;

    public ValueTask DisposeAsync() => throw new NotImplementedException();

    public IAsyncEnumerable<EventStoreRecord> Records(CancellationToken stoppingToken = new CancellationToken()) => throw new NotImplementedException();

    public Task<IReadOnlyList<RecordPosition>> Track(EventStoreRecord record, CancellationToken cancellationToken = new CancellationToken()) =>
        Task.FromResult<IReadOnlyList<RecordPosition>>(new List<RecordPosition>());

    public Task<IReadOnlyList<RecordPosition>> Commit(EventStoreRecord record, CancellationToken cancellationToken = new CancellationToken()) =>
        Task.FromResult<IReadOnlyList<RecordPosition>>(new List<RecordPosition>());

    public Task<IReadOnlyList<RecordPosition>> CommitAll(CancellationToken cancellationToken = new CancellationToken()) =>
        Task.FromResult<IReadOnlyList<RecordPosition>>(new List<RecordPosition>());

    public Task<IReadOnlyList<RecordPosition>> GetLatestPositions(CancellationToken cancellationToken = new CancellationToken()) =>
        Task.FromResult<IReadOnlyList<RecordPosition>>(new List<RecordPosition>());
}