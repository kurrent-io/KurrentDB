// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ExplicitCallerInfoArgument
// ReSharper disable AccessToDisposedClosure

using Kurrent.Surge;
using Kurrent.Surge.Connectors;
using KurrentDB.Connectors.Control.Contracts;
using KurrentDB.Connectors.Management.Contracts.Events;
using KurrentDB.Connectors.Planes.Control;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Connectors.Tests.Planes.Control;

[Trait("Category", "ControlPlane")]
public class ConnectorsControlRegistryTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
    [Fact]
    public Task load_without_snapshot_starts_at_end_of_log_and_saves_it() => Fixture.TestWithTimeout(async cancellator => {
        var snapshotStreamId = Fixture.NewStreamId();
        var sut              = CreateRegistry(snapshotStreamId);
        var events           = await Fixture.ProduceTestEvents(Fixture.NewStreamId());

        var connectors = await sut.LoadSnapshot(cancellator.Token);

        connectors.Should().BeEmpty();
        connectors.Position.LogPosition.CommitPosition.Should().BeGreaterThanOrEqualTo(events.Last().Position.LogPosition.CommitPosition!.Value);

        var snapshot = await ReadSnapshot(snapshotStreamId, cancellator.Token);
        snapshot.LogPosition.Should().Be(connectors.Position.LogPosition.CommitPosition!.Value);
    });

    [Fact]
    public Task saved_snapshot_is_loaded_back() => Fixture.TestWithTimeout(async cancellator => {
        var sut         = CreateRegistry(Fixture.NewStreamId());
        var connectorId = Fixture.NewConnectorId();
        var connectors  = await sut.LoadSnapshot(cancellator.Token);
        var events      = await Fixture.ProduceTestEvents(Fixture.NewStreamId());

        connectors.Apply(new SurgeRecord {
            Value    = new ConnectorActivating { ConnectorId = connectorId, Revision = 1 },
            Position = events.Last().Position
        });

        await sut.SaveSnapshot(connectors);

        var loaded = await sut.LoadSnapshot(cancellator.Token);

        loaded.Should().ContainSingle(x => x.ConnectorId == ConnectorId.From(connectorId));
        loaded.Position.LogPosition.CommitPosition.Should().Be(connectors.Position.LogPosition.CommitPosition);
    });

    ConnectorsControlRegistry CreateRegistry(string snapshotStreamId) =>
        ActivatorUtilities.CreateInstance<ConnectorsControlRegistry>(
            Fixture.NodeServices,
            new ConnectorsControlRegistryOptions { SnapshotStreamId = snapshotStreamId }
        );

    async Task<ActivatedConnectorsSnapshot> ReadSnapshot(string snapshotStreamId, CancellationToken ct) {
        var snapshot = await Fixture.Reader.ReadLastStreamRecord(snapshotStreamId, ct);
        snapshot.Value.Should().BeOfType<ActivatedConnectorsSnapshot>();
        return (ActivatedConnectorsSnapshot)snapshot.Value;
    }
}
