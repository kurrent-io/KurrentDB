// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge;
using KurrentDB.Connectors.Management.Contracts;
using KurrentDB.Connectors.Management.Contracts.Events;
using KurrentDB.Connectors.Planes.Control.Model;

namespace KurrentDB.Connectors.Tests.Planes.Control;

[Trait("Category", "ControlPlane")]
public class ActiveConnectorsTests {
    const string StartPositionKey = "Subscription:StartPosition";

    [Fact]
    public void running_removes_start_position_set_by_activating() {
        var sut = new ActiveConnectors();

        sut.Apply(Record(new ConnectorActivating { ConnectorId = "c1", StartFrom = new StartFromPosition { LogPosition = 42 } }, 10));
        sut["c1"].Settings.Should().ContainKey(StartPositionKey);

        sut.Apply(Record(new ConnectorRunning { ConnectorId = "c1" }, 20));
        sut["c1"].Settings.Should().NotContainKey(StartPositionKey);
    }

    [Fact]
    public void running_for_unknown_connector_is_ignored() {
        var sut = new ActiveConnectors();

        sut.Apply(Record(new ConnectorRunning { ConnectorId = "c1" }, 10));

        sut.Should().BeEmpty();
        sut.Position.LogPosition.CommitPosition.Should().Be(10);
    }

    [Fact]
    public void position_only_moves_forward() {
        var sut = new ActiveConnectors();

        sut.Apply(Record(new ConnectorDeactivating { ConnectorId = "c1" }, 20));
        sut.Apply(Record(new ConnectorDeactivating { ConnectorId = "c1" }, 10));

        sut.Position.LogPosition.CommitPosition.Should().Be(20);
    }

    static SurgeRecord Record(object value, ulong position) =>
        new() { Value = value, Position = LogPosition.From(position) };
}
