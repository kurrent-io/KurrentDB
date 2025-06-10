// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ExplicitCallerInfoArgument
// ReSharper disable AccessToDisposedClosure

using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Producers;
using KurrentDB.Connectors.Control.Contracts;
using KurrentDB.Connectors.Management.Contracts.Events;
using KurrentDB.Connectors.Planes.Control;
using Microsoft.Extensions.DependencyInjection;
using static KurrentDB.Connectors.Planes.ConnectorsFeatureConventions;

namespace KurrentDB.Connectors.Tests.Planes.Control;

[Trait("Category", "ControlPlane")]
public class ConnectorsControlRegistryTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
    [Fact]
    public Task returns_active_connectors_and_updates_snapshot() => Fixture.TestWithTimeout(async cancellator => {
        // Arrange
        var sut = Fixture.NodeServices.GetRequiredService<ConnectorsControlRegistry>();
        var connectorId = Fixture.NewConnectorId();

        var streamId = Streams.ManagementStreamTemplate.GetStream(Fixture.NewStreamId());
        // Act & Assert
        // Initially, there is no connector, and no snapshot is empty
        var result = await sut.GetConnectors(cancellator.Token);
        result.Connectors.Should().BeEmpty();
        var snapshot = await ReadSnapshot(cancellator.Token);
        snapshot.Connectors.Should().BeEmpty();
        snapshot.Should().NotBeNull();
        // Now, a connector has been created and activated
        var connectorMessages       = await ProduceConnectorEvents(streamId, connectorId);
        var connectorRunningMessage = connectorMessages.Last();
        result = await sut.GetConnectors(cancellator.Token);
        result.Connectors.Should().NotBeEmpty();
        snapshot = await ReadSnapshot(cancellator.Token);
        snapshot.LogPosition.Should().Be(connectorRunningMessage.Position);
        // Non-connector related events should not affect the snapshot
        await Fixture.ProduceTestEvents(Fixture.NewStreamId(), 5);
        result = await sut.GetConnectors(cancellator.Token);
        result.Connectors.Should().NotBeEmpty();
        snapshot = await ReadSnapshot(cancellator.Token);
        snapshot.LogPosition.Should().Be(connectorRunningMessage.Position);
    });

    async Task<ActivatedConnectorsSnapshot> ReadSnapshot(CancellationToken ct) {
        var snapshot = await Fixture.Reader.ReadLastStreamRecord(Streams.ControlConnectorsRegistryStream, ct);
        snapshot.Should().NotBeNull();
        snapshot.Value.Should().BeOfType<ActivatedConnectorsSnapshot>();
        return (ActivatedConnectorsSnapshot)snapshot.Value;
    }
    async Task<List<ProduceResult>> ProduceConnectorEvents(string streamId, string connectorId) {
        var tasks = new List<Task<ProduceResult>> {
            ProduceActivating(streamId, connectorId),
            ProduceRunning(streamId, connectorId)
        };
        await Task.WhenAll(tasks);
        return tasks.Select(x => x.Result).ToList();
    }
    async Task<ProduceResult> ProduceActivating(string streamId, string connectorId) {
        var activating = new ConnectorActivating {
            ConnectorId = connectorId,
            Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
        };
        var message = Message.Builder.Value(activating).Create();
        var request = ProduceRequest.Builder.Message(message).Stream(streamId).Create();
        var result  = await Fixture.Producer.Produce(request);
        return result;
    }
    async Task<ProduceResult> ProduceRunning(string streamId, string connectorId) {
        var running = new ConnectorRunning {
            ConnectorId = connectorId,
            Timestamp   = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
        };
        var message = Message.Builder.Value(running).Create();
        var request = ProduceRequest.Builder.Message(message).Stream(streamId).Create();
        var result  = await Fixture.Producer.Produce(request);
        return result;
    }
}
