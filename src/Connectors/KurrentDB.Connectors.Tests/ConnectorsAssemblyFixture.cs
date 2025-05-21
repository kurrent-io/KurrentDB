// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge;
using Kurrent.Surge.Consumers;
using Kurrent.Surge.DataProtection;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Connectors.Infrastructure;
using KurrentDB.Connectors.Management.Contracts.Events;
using KurrentDB.Connectors.Planes.Management;
using KurrentDB.Connectors.Tests;
using KurrentDB.Surge.Testing.Fixtures;
using KurrentDB.Surge.Testing.Xunit.Extensions.AssemblyFixture;
using Microsoft.Extensions.DependencyInjection;
using WithExtension = KurrentDB.Surge.Testing.Extensions.WithExtension;

[assembly: TestFramework(XunitTestFrameworkWithAssemblyFixture.TypeName, XunitTestFrameworkWithAssemblyFixture.AssemblyName)]
[assembly: AssemblyFixture(typeof(ConnectorsAssemblyFixture))]

namespace KurrentDB.Connectors.Tests;

[PublicAPI]
public partial class ConnectorsAssemblyFixture : SystemComponentsAssemblyFixture {
    public IServiceProvider  ConnectorServices { get; private set; } = null!;

    public ISnapshotProjectionsStore SnapshotProjectionsStore => NodeServices.GetRequiredService<ISnapshotProjectionsStore>();
    public IDataProtector            DataProtector            => NodeServices.GetRequiredService<IDataProtector>();

    public ConnectorsCommandApplication CommandApplication { get; private set; } = null!;

    SequenceIdGenerator SequenceIdGenerator { get; } = new();

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
            SurgeRecord.None,
            FakeConsumer.Instance,
            StateStore,
            CreateLogger("TestLogger"),
            SchemaRegistry,
            cancellationToken);

        return context;
    }

    public async ValueTask<SurgeRecord> CreateRecord<T>(T message, SchemaDataFormat schemaType = SchemaDataFormat.Json, string? streamId = null) {
        var schemaInfo = SchemaRegistry.CreateSchemaInfo<T>(schemaType);

        // Tweaks so we don't have conflict with the connector plugin that already registered those messages.
        switch (message)
        {
	        case ConnectorCreated:
		        schemaInfo = schemaInfo with { SchemaName = "$conn-mngt-connector-created" };
		        break;
	        case ConnectorActivating:
		        schemaInfo = schemaInfo with { SchemaName = "$conn-mngt-connector-activating" };
		        break;
	        case ConnectorDeactivating:
		        schemaInfo = schemaInfo with { SchemaName = "$conn-mngt-connector-deactivating" };
		        break;
	        case ConnectorDeleted:
		        schemaInfo = schemaInfo with { SchemaName = "$conn-mngt-connector-deleted" };
		        break;
	        case ConnectorFailed:
		        schemaInfo = schemaInfo with { SchemaName = "$conn-mngt-connector-failed" };
		        break;
	        case ConnectorReconfigured:
		        schemaInfo = schemaInfo with { SchemaName = "$conn-mngt-connector-reconfigured" };
		        break;
	        case ConnectorRenamed:
		        schemaInfo = schemaInfo with { SchemaName = "$conn-mngt-connector-renamed" };
		        break;
	        case ConnectorRunning:
		        schemaInfo = schemaInfo with { SchemaName = "$conn-mngt-connector-running" };
		        break;
	        case ConnectorStopped:
		        schemaInfo = schemaInfo with { SchemaName = "$conn-mngt-connector-stopped" };
		        break;
        }

        var data = await ((ISchemaSerializer)SchemaRegistry).Serialize(message, schemaInfo);

        var sequenceId = SequenceIdGenerator.FetchNext().Value;

        var headers = new Headers();
        schemaInfo.InjectIntoHeaders(headers);

        return new SurgeRecord {
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

    public IAsyncEnumerable<SurgeRecord> Records(CancellationToken stoppingToken = new CancellationToken()) => throw new NotImplementedException();

    public Task<IReadOnlyList<RecordPosition>> Track(SurgeRecord record, CancellationToken cancellationToken = new CancellationToken()) =>
        Task.FromResult<IReadOnlyList<RecordPosition>>(new List<RecordPosition>());

    public Task<IReadOnlyList<RecordPosition>> Commit(SurgeRecord record, CancellationToken cancellationToken = new CancellationToken()) =>
        Task.FromResult<IReadOnlyList<RecordPosition>>(new List<RecordPosition>());

    public Task<IReadOnlyList<RecordPosition>> CommitAll(CancellationToken cancellationToken = new CancellationToken()) =>
        Task.FromResult<IReadOnlyList<RecordPosition>>(new List<RecordPosition>());

    public Task<IReadOnlyList<RecordPosition>> GetLatestPositions(CancellationToken cancellationToken = new CancellationToken()) =>
        Task.FromResult<IReadOnlyList<RecordPosition>>(new List<RecordPosition>());
}
