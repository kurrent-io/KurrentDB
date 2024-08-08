using EventStore.Connect.Readers;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connectors.Management.Contracts;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Processors;
using EventStore.Streaming.Producers;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using ConnectContracts = EventStore.Streaming.Contracts;
using ConnectorState = EventStore.Connectors.Management.Contracts.ConnectorState;

namespace EventStore.Connectors.Management.Projectors;

public record ConnectorsStateProjectorOptions {
    public ConsumeFilter Filter           { get; init; }
    public StreamId      SnapshotStreamId { get; init; }
}

/// <summary>
/// Responsible for projecting the current state of all connectors in the system.
/// </summary>
public class ConnectorsStateProjector : ProcessingModule {
    ConnectorsStateProjectorOptions Options                  { get; }
    SystemReader                    Reader                   { get; }
    IPublisher                      Publisher                { get; }
    bool                            ConfiguredSnapshotStream { get; set; }

    public ConnectorsStateProjector(
        ConnectorsStateProjectorOptions options,
        IPublisher publisher,
        Func<SystemReaderBuilder> getReaderBuilder
    ) {
        Options   = options;
        Publisher = publisher;
        Reader    = getReaderBuilder().ReaderId("connectors-state-projector-rdx").Create();

        Process<ConnectorCreated>(async (evt, ctx) =>
            await UpdateConnector(connector => connector with {
                    Name = evt.Name,
                    InstanceType = evt.Settings["InstanceTypeName"],
                    State = ConnectorState.Stopped,
                    StateTimestamp = evt.Timestamp,
                    Settings = evt.Settings,
                    SettingsTimestamp = evt.Timestamp
                },
                evt.ConnectorId,
                ctx));

        Process<ConnectorReconfigured>(async (evt, ctx) =>
            await UpdateConnector(connector => connector with {
                    Settings = evt.Settings,
                    SettingsTimestamp = evt.Timestamp
                },
                evt.ConnectorId,
                ctx));

        Process<ConnectorRenamed>(async (evt, ctx) =>
            await UpdateConnector(connector => connector with {
                    Name = evt.Name
                },
                evt.ConnectorId,
                ctx));

        Process<ConnectorActivating>(async (evt, ctx) =>
            await UpdateConnector(connector => connector with {
                    State = ConnectorState.Activating,
                    StateTimestamp = evt.Timestamp,
                    Settings = evt.Settings,
                    SettingsTimestamp = evt.Timestamp
                },
                evt.ConnectorId,
                ctx));

        Process<ConnectorDeactivating>(async (evt, ctx) =>
            await UpdateConnector(connector => connector with {
                    State = ConnectorState.Deactivating,
                    StateTimestamp = evt.Timestamp
                },
                evt.ConnectorId,
                ctx));

        Process<ConnectorRunning>(async (evt, ctx) =>
            await UpdateConnector(connector => connector with {
                    State = ConnectorState.Running,
                    StateTimestamp = evt.Timestamp
                },
                evt.ConnectorId,
                ctx));

        Process<ConnectorStopped>(async (evt, ctx) =>
            await UpdateConnector(connector => connector with {
                    State = ConnectorState.Stopped,
                    StateTimestamp = evt.Timestamp
                },
                evt.ConnectorId,
                ctx));

        Process<ConnectorFailed>(async (evt, ctx) =>
            await UpdateConnector(connector => connector with {
                    State = ConnectorState.Stopped,
                    StateTimestamp = evt.Timestamp
                },
                evt.ConnectorId,
                ctx));

        Process<ConnectorDeleted>(async (evt, ctx) => {
            var (snapshot, position) = await LoadSnapshot(ctx);

            var updatedSnapshot = new ConnectorsSnapshot {
                Connectors = {
                    snapshot.Connectors
                        .Where(connector => connector.ConnectorId != evt.ConnectorId)
                        .ToList()
                }
            };

            UpdateSnapshot(updatedSnapshot, position, ctx);
        });

        Process<ConnectContracts.Consumers.Checkpoint>(async (checkpoint, ctx) =>
            await UpdateConnector(connector => connector with {
                    Position = checkpoint.LogPosition
                },
                ExtractConnectorIdFrom(checkpoint.StreamId),
                ctx));

        // TODO JC: Is this the best way to configure the state snapshot stream?
        Process(async ctx => {
            if (ConfiguredSnapshotStream) return;

            await TryConfigureSnapshotStream(ctx.CancellationToken);
            ConfiguredSnapshotStream = true;

            return;

            Task TryConfigureSnapshotStream(CancellationToken cancellationToken) {
                var stream   = Options.SnapshotStreamId;
                var metadata = new StreamMetadata(maxCount: 1);

                return Publisher
                    .SetStreamMetadata(stream, metadata, cancellationToken: cancellationToken)
                    .OnError(ex => ctx.Logger.LogError(ex, "Failed to configure stream {Stream}", stream))
                    .Then(state =>
                            state.Logger.LogDebug("Stream {Stream} configured {Metadata}",
                                state.Stream,
                                state.Metadata),
                        (ctx.Logger, Stream: stream, Metadata: metadata));
            }
        });
    }

    static string ExtractConnectorIdFrom(string streamId) => streamId.Split('/')[1];

    async Task UpdateConnector(
        Func<Connector, Connector> updateConnector,
        string connectorId,
        RecordContext ctx
    ) {
        var (snapshot, position) = await LoadSnapshot(ctx);

        var connector = snapshot.Connectors.FirstOrDefault(conn => conn.ConnectorId == connectorId,
            new ConnectorsSnapshot.Types.Connector { ConnectorId = connectorId });

        var updatedSnapshot = new ConnectorsSnapshot {
            Connectors = {
                snapshot.Connectors.Where(conn => conn.ConnectorId != connectorId)
                    .Append(Connector.From(connector).Apply(updateConnector))
                    .ToList()
            }
        };

        UpdateSnapshot(updatedSnapshot, position, ctx);
    }

    async Task<(ConnectorsSnapshot, RecordPosition)> LoadSnapshot(RecordContext ctx) {
        try {
            var snapshotRecord = await Reader.ReadLastStreamRecord(Options.SnapshotStreamId, ctx.CancellationToken);

            return snapshotRecord.Value is not ConnectorsSnapshot snapshot
                ? (new(), snapshotRecord.Position)
                : (snapshot, snapshotRecord.Position);
        } catch (Exception ex) {
            ctx.Logger.LogError(ex, "Failed to load connectors snapshot");
            throw;
        }
    }

    void UpdateSnapshot(ConnectorsSnapshot snapshot, RecordPosition expectedPosition, RecordContext ctx) {
        try {
            var produceRequest = ProduceRequest.Builder
                .Message(snapshot)
                .Stream(Options.SnapshotStreamId)
                .ExpectedStreamRevision(expectedPosition.StreamRevision)
                .Create();

            ctx.Output(produceRequest);
        } catch (Exception ex) {
            ctx.Logger.LogError(ex, "Failed to update connectors snapshot");
            throw;
        }
    }

    record Connector(
        string ConnectorId,
        string Name,
        string InstanceType,
        ConnectorState State,
        Timestamp StateTimestamp,
        MapField<string, string> Settings,
        Timestamp SettingsTimestamp,
        ulong? Position
    ) {
        public static Connector From(ConnectorsSnapshot.Types.Connector connector) => new(connector.ConnectorId,
            connector.Name,
            connector.InstanceType,
            connector.State,
            connector.StateTimestamp,
            connector.Settings,
            connector.SettingsTimestamp,
            connector.Position);

        public ConnectorsSnapshot.Types.Connector Apply(Func<Connector, Connector> projection) =>
            projection(this).ToSnapshot();

        ConnectorsSnapshot.Types.Connector ToSnapshot() => new() {
            ConnectorId       = ConnectorId,
            Name              = Name,
            InstanceType      = InstanceType,
            State             = State,
            StateTimestamp    = StateTimestamp,
            Settings          = { Settings },
            SettingsTimestamp = SettingsTimestamp,
            Position          = Position
        };
    }
}