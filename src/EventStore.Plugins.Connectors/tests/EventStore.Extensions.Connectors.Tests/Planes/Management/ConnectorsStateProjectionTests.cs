#pragma warning disable EXTEXP0004

using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Connectors.Management.Contracts.Queries;
using EventStore.Connectors.Management.Data;
using EventStore.Extensions.Connectors.Tests;
using EventStore.Streaming.Processors;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace EventStore.Connectors.Tests.Management;

public class ConnectorsStateProjectionTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture) : ConnectorsIntegrationTests(output, fixture) {
    [Fact]
    public Task projection_updates_on_ConnectorCreated() => Fixture.TestWithTimeout(async cancellator => {
        // Arrange
        var connectorId     = Fixture.NewConnectorId();
        var streamId        = Fixture.NewIdentifier();
        var createTimestamp = Fixture.TimeProvider.GetUtcNow().ToTimestamp();
        var projection      = new ConnectorsStateProjection(Fixture.SnapshotProjectionsStore, streamId);

        var cmd = new ConnectorCreated {
            ConnectorId = connectorId,
            Name        = "Logger Sink",
            Settings = {
                { "instanceTypeName", "logger-sink" }
            },
            Timestamp = createTimestamp
        };

        var expectedSnapshot = new ConnectorsSnapshot {
            Connectors = {
                new Connector {
                    ConnectorId        = cmd.ConnectorId,
                    InstanceTypeName   = cmd.Settings["instanceTypeName"],
                    Name               = cmd.Name,
                    State              = ConnectorState.Stopped,
                    CreateTime         = createTimestamp,
                    UpdateTime         = createTimestamp,
                    StateUpdateTime    = createTimestamp,
                    SettingsUpdateTime = createTimestamp,
                    DeleteTime         = null,
                    Position           = null,
                    ErrorDetails       = null,
                    PositionUpdateTime = null,
                    Settings = {
                        { "instanceTypeName", "logger-sink" }
                    }
                }
            }
        };

        // Act
        await projection.ProcessRecord(await RecordContextFor(cmd, cancellator.Token));

        // Assert
        var store = await Fixture.SnapshotProjectionsStore.LoadSnapshot<ConnectorsSnapshot>(streamId);

        store.Snapshot.Connectors.Should().BeEquivalentTo(expectedSnapshot.Connectors);
    });

    [Fact]
    public Task projection_updates_on_ConnectorReconfigured() => Fixture.TestWithTimeout(async cancellator => {
        // Arrange
        var connectorId = Fixture.NewConnectorId();
        var streamId    = Fixture.NewIdentifier();
        var projection  = new ConnectorsStateProjection(Fixture.SnapshotProjectionsStore, streamId);

        var createTimestamp = Fixture.TimeProvider.GetUtcNow().ToTimestamp();

        var createdCommand = new ConnectorCreated {
            ConnectorId = connectorId,
            Name        = "Logger Sink",
            Settings = {
                { "instanceTypeName", "logger-sink" },
                { "logging:enabled", "true" }
            },
            Timestamp = createTimestamp
        };

        var reconfigureTimestamp = Fixture.TimeProvider.GetUtcNow().AddDays(1).ToTimestamp();

        var reconfigureCommand = new ConnectorReconfigured {
            ConnectorId = connectorId,
            Settings = {
                { "logging:enabled", "false" }
            },
            Timestamp = reconfigureTimestamp
        };

        var expectedSnapshot = new ConnectorsSnapshot {
            Connectors = {
                new Connector {
                    ConnectorId        = reconfigureCommand.ConnectorId,
                    InstanceTypeName   = createdCommand.Settings["instanceTypeName"],
                    Name               = createdCommand.Name,
                    State              = ConnectorState.Stopped,
                    CreateTime         = createTimestamp,
                    UpdateTime         = reconfigureTimestamp,
                    SettingsUpdateTime = reconfigureTimestamp,
                    StateUpdateTime    = createTimestamp,
                    DeleteTime         = null,
                    Position           = null,
                    ErrorDetails       = null,
                    PositionUpdateTime = null,
                    Settings = {
                        { "logging:enabled", "false" },
                    }
                }
            }
        };

        // Act
        await projection.ProcessRecord(await RecordContextFor(createdCommand, cancellator.Token));
        Fixture.TimeProvider.Advance(TimeSpan.FromDays(1));
        await projection.ProcessRecord(await RecordContextFor(reconfigureCommand, cancellator.Token));

        // Assert
        var store = await Fixture.SnapshotProjectionsStore.LoadSnapshot<ConnectorsSnapshot>(streamId);
        store.Snapshot.Connectors.Should().BeEquivalentTo(expectedSnapshot.Connectors);
    });

    [Fact]
    public Task projection_updates_on_ConnectorRenamed() => Fixture.TestWithTimeout(async cancellator => {
        // Arrange
        var connectorId = Fixture.NewConnectorId();
        var streamId    = Fixture.NewIdentifier();
        var projection  = new ConnectorsStateProjection(Fixture.SnapshotProjectionsStore, streamId);

        var createTimestamp = Fixture.TimeProvider.GetUtcNow().ToTimestamp();

        var createdCommand = new ConnectorCreated {
            ConnectorId = connectorId,
            Name        = "Logger Sink",
            Settings = {
                { "instanceTypeName", "logger-sink" }
            },
            Timestamp = createTimestamp
        };

        var renameTimestamp = Fixture.TimeProvider.GetUtcNow().AddDays(1).ToTimestamp();

        var renameCommand = new ConnectorRenamed {
            ConnectorId = connectorId,
            Timestamp   = renameTimestamp,
            Name        = "New Logger Sink"
        };

        var expectedSnapshot = new ConnectorsSnapshot {
            Connectors = {
                new Connector {
                    ConnectorId        = renameCommand.ConnectorId,
                    InstanceTypeName   = createdCommand.Settings["instanceTypeName"],
                    Name               = renameCommand.Name,
                    State              = ConnectorState.Stopped,
                    CreateTime         = createTimestamp,
                    UpdateTime         = renameTimestamp,
                    SettingsUpdateTime = createTimestamp,
                    StateUpdateTime    = createTimestamp,
                    DeleteTime         = null,
                    Position           = null,
                    ErrorDetails       = null,
                    PositionUpdateTime = null,
                    Settings = {
                        { "instanceTypeName", createdCommand.Settings["instanceTypeName"] }
                    }
                }
            }
        };

        // Act
        await projection.ProcessRecord(await RecordContextFor(createdCommand, cancellator.Token));
        Fixture.TimeProvider.Advance(TimeSpan.FromDays(1));
        await projection.ProcessRecord(await RecordContextFor(renameCommand, cancellator.Token));

        // Assert
        var store = await Fixture.SnapshotProjectionsStore.LoadSnapshot<ConnectorsSnapshot>(streamId);
        store.Snapshot.Connectors.Should().BeEquivalentTo(expectedSnapshot.Connectors);
    });

    [Fact]
    public Task projection_updates_on_ConnectorActivating() => Fixture.TestWithTimeout(async cancellator => {
        // Arrange
        var connectorId = Fixture.NewConnectorId();
        var streamId    = Fixture.NewIdentifier();
        var projection  = new ConnectorsStateProjection(Fixture.SnapshotProjectionsStore, streamId);

        var createTimestamp = Fixture.TimeProvider.GetUtcNow().ToTimestamp();

        var createdCommand = new ConnectorCreated {
            ConnectorId = connectorId,
            Name        = "Logger Sink",
            Settings = {
                { "instanceTypeName", "logger-sink" }
            },
            Timestamp = createTimestamp
        };

        var activateTimestamp = Fixture.TimeProvider.GetUtcNow().AddDays(1).ToTimestamp();

        var activateCommand = new ConnectorActivating {
            ConnectorId = connectorId,
            Timestamp   = activateTimestamp
        };

        var expectedSnapshot = new ConnectorsSnapshot {
            Connectors = {
                new Connector {
                    ConnectorId        = activateCommand.ConnectorId,
                    InstanceTypeName   = createdCommand.Settings["instanceTypeName"],
                    Name               = createdCommand.Name,
                    State              = ConnectorState.Activating,
                    CreateTime         = createTimestamp,
                    UpdateTime         = activateTimestamp,
                    SettingsUpdateTime = createTimestamp,
                    StateUpdateTime    = activateTimestamp,
                    DeleteTime         = null,
                    Position           = null,
                    ErrorDetails       = null,
                    PositionUpdateTime = null,
                    Settings = {
                        { "instanceTypeName", createdCommand.Settings["instanceTypeName"] }
                    }
                }
            }
        };

        // Act
        await projection.ProcessRecord(await RecordContextFor(createdCommand, cancellator.Token));
        Fixture.TimeProvider.Advance(TimeSpan.FromDays(1));
        await projection.ProcessRecord(await RecordContextFor(activateCommand, cancellator.Token));

        // Assert
        var store = await Fixture.SnapshotProjectionsStore.LoadSnapshot<ConnectorsSnapshot>(streamId);
        store.Snapshot.Connectors.Should().BeEquivalentTo(expectedSnapshot.Connectors);
    });

    [Fact]
    public Task projection_updates_on_ConnectorDeactivating() => Fixture.TestWithTimeout(async cancellator => {
        // Arrange
        var connectorId = Fixture.NewConnectorId();
        var streamId    = Fixture.NewIdentifier();
        var projection  = new ConnectorsStateProjection(Fixture.SnapshotProjectionsStore, streamId);

        var createTimestamp = Fixture.TimeProvider.GetUtcNow().ToTimestamp();

        var createdCommand = new ConnectorCreated {
            ConnectorId = connectorId,
            Name        = "Logger Sink",
            Settings = {
                { "instanceTypeName", "logger-sink" }
            },
            Timestamp = createTimestamp
        };

        var deactivateTimestamp = Fixture.TimeProvider.GetUtcNow().AddDays(1).ToTimestamp();

        var deactivateCommand = new ConnectorDeactivating {
            ConnectorId = connectorId,
            Timestamp   = deactivateTimestamp
        };

        var expectedSnapshot = new ConnectorsSnapshot {
            Connectors = {
                new Connector {
                    ConnectorId        = deactivateCommand.ConnectorId,
                    InstanceTypeName   = createdCommand.Settings["instanceTypeName"],
                    Name               = createdCommand.Name,
                    State              = ConnectorState.Deactivating,
                    CreateTime         = createTimestamp,
                    UpdateTime         = deactivateTimestamp,
                    SettingsUpdateTime = createTimestamp,
                    StateUpdateTime    = deactivateTimestamp,
                    DeleteTime         = null,
                    Position           = null,
                    ErrorDetails       = null,
                    PositionUpdateTime = null,
                    Settings = {
                        { "instanceTypeName", createdCommand.Settings["instanceTypeName"] }
                    }
                }
            }
        };

        // Act
        await projection.ProcessRecord(await RecordContextFor(createdCommand, cancellator.Token));
        Fixture.TimeProvider.Advance(TimeSpan.FromDays(1));
        await projection.ProcessRecord(await RecordContextFor(deactivateCommand, cancellator.Token));

        // Assert
        var store = await Fixture.SnapshotProjectionsStore.LoadSnapshot<ConnectorsSnapshot>(streamId);
        store.Snapshot.Connectors.Should().BeEquivalentTo(expectedSnapshot.Connectors);
    });

    [Fact]
    public Task projection_updates_on_ConnectorRunning() => Fixture.TestWithTimeout(async cancellator => {
        // Arrange
        var connectorId = Fixture.NewConnectorId();
        var streamId    = Fixture.NewIdentifier();
        var projection  = new ConnectorsStateProjection(Fixture.SnapshotProjectionsStore, streamId);

        var createTimestamp = Fixture.TimeProvider.GetUtcNow().ToTimestamp();

        var createdCommand = new ConnectorCreated {
            ConnectorId = connectorId,
            Name        = "Logger Sink",
            Settings = {
                { "instanceTypeName", "logger-sink" }
            },
            Timestamp = createTimestamp
        };

        var runningTimestamp = Fixture.TimeProvider.GetUtcNow().AddDays(1).ToTimestamp();

        var runningCommand = new ConnectorRunning {
            ConnectorId = connectorId,
            Timestamp   = runningTimestamp
        };

        var expectedSnapshot = new ConnectorsSnapshot {
            Connectors = {
                new Connector {
                    ConnectorId        = runningCommand.ConnectorId,
                    InstanceTypeName   = createdCommand.Settings["instanceTypeName"],
                    Name               = createdCommand.Name,
                    State              = ConnectorState.Running,
                    CreateTime         = createTimestamp,
                    UpdateTime         = runningTimestamp,
                    SettingsUpdateTime = createTimestamp,
                    StateUpdateTime    = runningTimestamp,
                    DeleteTime         = null,
                    Position           = null,
                    ErrorDetails       = null,
                    PositionUpdateTime = null,
                    Settings = {
                        { "instanceTypeName", createdCommand.Settings["instanceTypeName"] }
                    }
                }
            }
        };

        // Act
        await projection.ProcessRecord(await RecordContextFor(createdCommand, cancellator.Token));
        Fixture.TimeProvider.Advance(TimeSpan.FromDays(1));
        await projection.ProcessRecord(await RecordContextFor(runningCommand, cancellator.Token));

        // Assert
        var store = await Fixture.SnapshotProjectionsStore.LoadSnapshot<ConnectorsSnapshot>(streamId);
        store.Snapshot.Connectors.Should().BeEquivalentTo(expectedSnapshot.Connectors);
    });

    [Fact]
    public Task projection_updates_on_ConnectorStopped() => Fixture.TestWithTimeout(async cancellator => {
        // Arrange
        var connectorId = Fixture.NewConnectorId();
        var streamId    = Fixture.NewIdentifier();
        var projection  = new ConnectorsStateProjection(Fixture.SnapshotProjectionsStore, streamId);

        var createTimestamp = Fixture.TimeProvider.GetUtcNow().ToTimestamp();

        var createdCommand = new ConnectorCreated {
            ConnectorId = connectorId,
            Name        = "Logger Sink",
            Settings = {
                { "instanceTypeName", "logger-sink" }
            },
            Timestamp = createTimestamp
        };

        var stopTimestamp = Fixture.TimeProvider.GetUtcNow().AddDays(1).ToTimestamp();

        var stopCommand = new ConnectorStopped {
            ConnectorId = connectorId,
            Timestamp   = stopTimestamp
        };

        var expectedSnapshot = new ConnectorsSnapshot {
            Connectors = {
                new Connector {
                    ConnectorId        = stopCommand.ConnectorId,
                    InstanceTypeName   = createdCommand.Settings["instanceTypeName"],
                    Name               = createdCommand.Name,
                    State              = ConnectorState.Stopped,
                    CreateTime         = createTimestamp,
                    UpdateTime         = stopTimestamp,
                    SettingsUpdateTime = createTimestamp,
                    StateUpdateTime    = stopTimestamp,
                    DeleteTime         = null,
                    Position           = null,
                    ErrorDetails       = null,
                    PositionUpdateTime = null,
                    Settings = {
                        { "instanceTypeName", createdCommand.Settings["instanceTypeName"] }
                    }
                }
            }
        };

        // Act
        await projection.ProcessRecord(await RecordContextFor(createdCommand, cancellator.Token));
        Fixture.TimeProvider.Advance(TimeSpan.FromDays(1));
        await projection.ProcessRecord(await RecordContextFor(stopCommand, cancellator.Token));

        // Assert
        var store = await Fixture.SnapshotProjectionsStore.LoadSnapshot<ConnectorsSnapshot>(streamId);
        store.Snapshot.Connectors.Should().BeEquivalentTo(expectedSnapshot.Connectors);
    });

    [Fact]
    public Task projection_updates_on_ConnectorFailed() => Fixture.TestWithTimeout(async cancellator => {
        // Arrange
        var connectorId     = Fixture.NewConnectorId();
        var streamId        = Fixture.NewIdentifier();
        var createTimestamp = Fixture.TimeProvider.GetUtcNow().ToTimestamp();
        var failTimestamp   = Fixture.TimeProvider.GetUtcNow().AddDays(1).ToTimestamp();
        var projection      = new ConnectorsStateProjection(Fixture.SnapshotProjectionsStore, streamId);

        var createdCommand = new ConnectorCreated {
            ConnectorId = connectorId,
            Name        = "Logger Sink",
            Settings = {
                { "instanceTypeName", "logger-sink" }
            },
            Timestamp = createTimestamp
        };

        var failCommand = new ConnectorFailed {
            ConnectorId = connectorId,
            Timestamp   = failTimestamp
        };

        var expectedSnapshot = new ConnectorsSnapshot {
            Connectors = {
                new Connector {
                    ConnectorId        = failCommand.ConnectorId,
                    InstanceTypeName   = createdCommand.Settings["instanceTypeName"],
                    Name               = createdCommand.Name,
                    State              = ConnectorState.Stopped,
                    CreateTime         = createTimestamp,
                    UpdateTime         = failTimestamp,
                    SettingsUpdateTime = createTimestamp,
                    StateUpdateTime    = failTimestamp,
                    DeleteTime         = null,
                    Position           = null,
                    ErrorDetails       = null,
                    PositionUpdateTime = null,
                    Settings = {
                        { "instanceTypeName", createdCommand.Settings["instanceTypeName"] }
                    }
                }
            }
        };

        // Act
        await projection.ProcessRecord(await RecordContextFor(createdCommand, cancellator.Token));
        Fixture.TimeProvider.Advance(TimeSpan.FromDays(1));
        await projection.ProcessRecord(await RecordContextFor(failCommand, cancellator.Token));

        // Assert
        var store = await Fixture.SnapshotProjectionsStore.LoadSnapshot<ConnectorsSnapshot>(streamId);
        store.Snapshot.Connectors.Should().BeEquivalentTo(expectedSnapshot.Connectors);
    });

    [Fact]
    public Task projection_updates_on_ConnectorDeleted() => Fixture.TestWithTimeout(async cancellator => {
        // Arrange
        var connectorId = Fixture.NewConnectorId();
        var streamId    = Fixture.NewIdentifier();
        var projection  = new ConnectorsStateProjection(Fixture.SnapshotProjectionsStore, streamId);

        var createTimestamp = Fixture.TimeProvider.GetUtcNow().ToTimestamp();

        var createdCommand = new ConnectorCreated {
            ConnectorId = connectorId,
            Name        = "Logger Sink",
            Settings = {
                { "instanceTypeName", "logger-sink" }
            },
            Timestamp = createTimestamp
        };

        var deleteTimestamp = Fixture.TimeProvider.GetUtcNow().AddDays(1).ToTimestamp();

        var deleteCommand = new ConnectorDeleted {
            ConnectorId = connectorId,
            Timestamp   = deleteTimestamp
        };

        var expectedSnapshot = new ConnectorsSnapshot {
            Connectors = {
                new Connector {
                    ConnectorId        = deleteCommand.ConnectorId,
                    InstanceTypeName   = createdCommand.Settings["instanceTypeName"],
                    Name               = createdCommand.Name,
                    State              = ConnectorState.Stopped,
                    CreateTime         = createTimestamp,
                    UpdateTime         = deleteTimestamp,
                    SettingsUpdateTime = createTimestamp,
                    StateUpdateTime    = createTimestamp,
                    DeleteTime         = deleteTimestamp,
                    Position           = null,
                    ErrorDetails       = null,
                    PositionUpdateTime = null,
                    Settings = {
                        { "instanceTypeName", createdCommand.Settings["instanceTypeName"] }
                    }
                }
            }
        };

        // Act
        await projection.ProcessRecord(await RecordContextFor(createdCommand, cancellator.Token));
        Fixture.TimeProvider.Advance(TimeSpan.FromDays(1));
        await projection.ProcessRecord(await RecordContextFor(deleteCommand, cancellator.Token));

        // Assert
        var store = await Fixture.SnapshotProjectionsStore.LoadSnapshot<ConnectorsSnapshot>(streamId);
        store.Snapshot.Connectors.Should().BeEquivalentTo(expectedSnapshot.Connectors);
    });

    async Task<RecordContext> RecordContextFor<T>(T cmd, CancellationToken cancellationToken) where T : IMessage {
        string connectorId = ((dynamic)cmd).ConnectorId;
        var record = await Fixture.CreateRecord(cmd);
        return Fixture.CreateRecordContext(connectorId, cancellationToken) with { Record = record };
    }
}