// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Connectors.Kafka;
using Kurrent.Surge.Connectors.Sinks;
using Kurrent.Surge.DataProtection;
using Kurrent.Surge.Processors;
using KurrentDB.Connectors.Infrastructure.Connect.Components.Connectors;
using KurrentDB.Connectors.Management.Contracts.Events;
using KurrentDB.Connectors.Management.Contracts.Queries;
using KurrentDB.Connectors.Planes.Management.Data;
using KurrentDB.Connectors.Planes.Management.Domain;
using Microsoft.Extensions.Configuration;

namespace KurrentDB.Connectors.Tests.Infrastructure;

public class DataProtectionTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
    [Fact]
    public Task data_protector_should_protect_and_unprotect_successfully() => Fixture.TestWithTimeout(async cts => {
        // Arrange
        IConnectorDataProtector connectorDataProtector = new ConnectorsMasterDataProtector(
            Fixture.DataProtector,
            new DataProtectionOptions {
                Token = "VALID_TOKEN",
            });

        const string key              = "Authentication:Password";
        const string value            = "plaintext";
        const string instanceTypeName = nameof(SinkOptions.InstanceTypeName);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> {
                [instanceTypeName] = nameof(KafkaSink),
                [key]              = value
            }!).Build();

        // Act
        configuration[key] = await Fixture.DataProtector.Protect(configuration[key]!, cts.Token);

        var unprotectedConfig = await connectorDataProtector.Unprotect(configuration, cts.Token);

        // Assert
        unprotectedConfig[key].Should().BeEquivalentTo(value);
    });

    [Fact]
    public Task data_protector_should_protect_and_store_command_settings_in_snapshot() => Fixture.TestWithTimeout(async cts => {
        // Arrange
        var connectorId = Fixture.NewConnectorId();
        var streamId    = Fixture.NewIdentifier();

        var createTimestamp        = Fixture.TimeProvider.GetUtcNow().ToTimestamp();
        var projection             = new ConnectorsStateProjection(Fixture.SnapshotProjectionsStore, streamId);

        IConnectorDataProtector connectorDataProtector = new ConnectorsMasterDataProtector(
            Fixture.DataProtector,
            new DataProtectionOptions {
                Token = "VALID_TOKEN",
            });

        const string key   = "Authentication:Password";
        const string value = "secret";

        var cmdSettings = new Dictionary<string, string> {
            { "instanceTypeName", "kafka-sink" },
            { key, value }
        };

        var settings = ConnectorSettings
            .From(cmdSettings!, connectorId)
            .Protect(connectorDataProtector.Protect)
            .AsDictionary();

        var cmd = new ConnectorCreated {
            ConnectorId = connectorId,
            Name        = "Kafka Sink",
            Settings    = { settings },
            Timestamp   = createTimestamp
        };

        // Act
        await projection.ProcessRecord(await RecordContextFor(cmd, cts.Token));

        // Assert
        var store = await Fixture.SnapshotProjectionsStore.LoadSnapshot<ConnectorsSnapshot>(streamId);

        store.Snapshot.Connectors.Should().ContainSingle();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(store.Snapshot.Connectors.First().Settings)
            .Build();

        var unprotected = await connectorDataProtector.Unprotect(configuration, cts.Token);

        unprotected[key].Should().BeEquivalentTo(value);
    });

    async Task<RecordContext> RecordContextFor<T>(T cmd, CancellationToken cancellationToken) where T : IMessage {
        string connectorId = ((dynamic)cmd).ConnectorId;
        var    record      = await Fixture.CreateRecord(cmd);
        return Fixture.CreateRecordContext(connectorId, cancellationToken) with { Record = record };
    }
}
