#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using DotNext.Collections.Generic;
using EventStore.Connect.Connectors;
using EventStore.Connectors.Control.Contracts;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Readers;
using EventStore.Streaming.Schema;
using Google.Protobuf.WellKnownTypes;

namespace EventStore.Connectors.Control.Coordination;

// public record Connector {
//     public string                      ConnectorId { get; init; }
//     public int                         Revision    { get; init; }
//     public Dictionary<string, string?> Settings    { get; init; } = [];
// }

public delegate Task<RegisteredConnector[]> GetActiveConnectors(CancellationToken cancellationToken);

public record RegisteredConnector(ConnectorId ConnectorId, int Revision, ConnectorSettings Settings) {
    public ConnectorResource Resource     { get; } = new(ConnectorId, Settings.NodeAffinity);
    public ClusterNodeState  NodeAffinity { get; } = Settings.NodeAffinity;
}

class ConnectorsRegistry {
    public ConnectorsRegistry(
        SystemReader reader,
        SystemProducer producer,
        ConsumeFilter connectorsConsumeFilter,
        StreamId snapshotStreamId,
        TimeProvider timeProvider
    ) {
        Reader                  = reader;
        Producer                = producer;
        ConnectorsConsumeFilter = connectorsConsumeFilter;
        SnapshotStreamId        = snapshotStreamId;
        TimeProvider            = timeProvider;
    }

    SystemProducer Producer                { get; }
    ConsumeFilter  ConnectorsConsumeFilter { get; }
    SystemReader   Reader                  { get; }
    StreamId       SnapshotStreamId        { get; }
    TimeProvider   TimeProvider            { get; }

    /// <summary>
    /// Asynchronously retrieves an array of active connectors registered in the system.
    /// </summary>
    /// <param name="cancellationToken">A CancellationToken to observe while waiting for the task to complete.</param>
    public async Task<RegisteredConnector[]> GetConnectors(CancellationToken cancellationToken) {
        var (checkpoint, state) = await LoadSnapshot(cancellationToken);

        var lastReadPosition = checkpoint;

        await foreach (var record in Reader.ReadForwards(checkpoint.LogPosition, ConnectorsConsumeFilter, cancellationToken: cancellationToken)) {
            switch (record.Value) {
                case ConnectorActivating evt:
                    state.Add(evt.ConnectorId, new(
                        evt.ConnectorId, evt.Revision,
                        new ConnectorSettings(evt.Settings.ToDictionary())
                    ));
                    break;

                case ConnectorDeactivating evt:
                    state.Remove(evt.ConnectorId);
                    break;
            }

            lastReadPosition = record.Position;
        }

        var result = state.Values.ToArray();

        // updates the snapshot every time the last record position is newer,
        // regardless of state changes
        if (lastReadPosition != checkpoint)
            await UpdateSnapshot(result, lastReadPosition);

        return result;

        async Task<(RecordPosition SnapshotPosition, Dictionary<ConnectorId, RegisteredConnector> State)> LoadSnapshot(CancellationToken ct) {
            var record = await Reader
                .ReadBackwards(ConsumeFilter.Streams(SnapshotStreamId), 1, ct)
                .FirstOrNullAsync(ct) ?? EventStoreRecord.None;

            if (record.Value is not RegisteredConnectors snapshot)
                return (record.Position with { LogPosition = LogPosition.Earliest }, []);

            var snapshotState = snapshot.Connectors.ToDictionary(
                x => ConnectorId.From(x.ConnectorId),
                x => new RegisteredConnector(
                    x.ConnectorId, x.Revision,
                    new ConnectorSettings(x.Settings.ToDictionary())
                )
            );

            return (record.Position, snapshotState);
        }

        async Task UpdateSnapshot(RegisteredConnector[] connectors, RecordPosition expectedPosition) {
            var newSnapshot = new RegisteredConnectors {
                Connectors = {
                    connectors.Select(
                        x => new Connector {
                            ConnectorId = x.ConnectorId,
                            Revision    = x.Revision,
                            Settings    = { x.Settings }
                        }
                    )
                },
                LogPosition = expectedPosition.LogPosition.CommitPosition!.Value,
                UpdatedAt   = TimeProvider.GetUtcNow().ToTimestamp()
            };

           await Producer.Send(
                SendRequest.Builder
                    .Message(newSnapshot, SchemaDefinitionType.Protobuf)
                    .Stream(SnapshotStreamId)
                    .ExpectedStreamRevision(expectedPosition.StreamRevision)
                    .Create()
            );
        }
    }
}