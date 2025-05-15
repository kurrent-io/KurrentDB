// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ExplicitCallerInfoArgument
// ReSharper disable AccessToDisposedClosure

using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Producers;
using KurrentDB.Connect.Producers.Configuration;
using KurrentDB.Connect.Readers.Configuration;
using KurrentDB.Connectors.Management.Contracts.Events;
using KurrentDB.Connectors.Planes.Control;
using Microsoft.Extensions.DependencyInjection;
using static KurrentDB.Connectors.Planes.ConnectorsFeatureConventions;

namespace KurrentDB.Connectors.Tests.Planes.Control;

[Trait("Category", "ControlPlane")]
public class ConnectorsControlRegistryTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
	[Fact(Skip = "Isolate is conflicting with the one below")]
    public Task updates_snapshot_with_no_active_connectors() => Fixture.TestWithTimeout(async cancellator => {
        // Arrange
        var getReaderBuilder   = Fixture.NodeServices.GetRequiredService<Func<SystemReaderBuilder>>();
        var getProducerBuilder = Fixture.NodeServices.GetRequiredService<Func<SystemProducerBuilder>>();

        var options = new ConnectorsControlRegistryOptions {
            Filter           = Filters.ManagementFilter,
            SnapshotStreamId = Fixture.NewStreamId() // Random stream ID to avoid test interference
        };

        var sut = new ConnectorsControlRegistry(options, getReaderBuilder, getProducerBuilder, Fixture.TimeProvider);

        // Act
        var result = await sut.GetConnectors(cancellator.Token);

        // Assert
        result.Connectors.Should().BeEmpty();
    });

    [Fact]
    public Task returns_active_connectors_and_updates_snapshot() => Fixture.TestWithTimeout(async cancellator => {
        // Arrange
        var getReaderBuilder   = Fixture.NodeServices.GetRequiredService<Func<SystemReaderBuilder>>();
        var getProducerBuilder = Fixture.NodeServices.GetRequiredService<Func<SystemProducerBuilder>>();

        var connectorId = Fixture.NewConnectorId();

        var options = new ConnectorsControlRegistryOptions {
            Filter           = Filters.ManagementFilter,
            SnapshotStreamId = Streams.ControlConnectorsRegistryStream
        };

        var sut = new ConnectorsControlRegistry(options, getReaderBuilder, getProducerBuilder, Fixture.TimeProvider);

        // Act
        var activatingMessage = Message.Builder
            .Value(new ConnectorActivating {
                ConnectorId = connectorId,
                Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
            })
            .Create();

        var runningMessage = Message.Builder
            .Value(new ConnectorRunning {
                ConnectorId = connectorId,
                Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
            })
            .Create();

        var produceResult = await Fixture.Producer.Produce(ProduceRequest.Builder
            .Messages(activatingMessage, runningMessage)
            .Stream(Streams.ManagementStreamTemplate.GetStream(Fixture.NewStreamId()))
            .Create());

        // Assert
        var result = await sut.GetConnectors(CancellationToken.None);

        result.Connectors.Should().ContainSingle();
        result.Position.Should().Be(produceResult.Position);
    });
}
