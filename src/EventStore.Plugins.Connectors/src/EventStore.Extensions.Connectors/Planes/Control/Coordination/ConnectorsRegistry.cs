#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using DotNext.Collections.Generic;
using EventStore.Connect.Connectors;
using EventStore.Connect.Producers;
using EventStore.Connect.Readers;
using EventStore.Connectors.Control.Contracts;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Readers;
using Google.Protobuf.WellKnownTypes;

namespace EventStore.Connectors.Control.Coordination;

public delegate Task<RegisteredConnector[]> GetActiveConnectors(CancellationToken cancellationToken);

public record RegisteredConnector(ConnectorId ConnectorId, int Revision, ConnectorSettings Settings) {
    public ConnectorResource Resource     { get; } = new(ConnectorId, Settings.NodeAffinity);
    public ClusterNodeState  NodeAffinity { get; } = Settings.NodeAffinity;
}

class ConnectorsRegistry {
    public ConnectorsRegistry(
        SystemReader reader,
        SystemProducer producer,
        ConsumeFilter filter,
        StreamId snapshotStreamId,
        TimeProvider time
    ) {
        Reader           = reader;
        Producer         = producer;
        Filter           = filter;
        SnapshotStreamId = snapshotStreamId;
        Time             = time;
    }

    SystemReader   Reader           { get; }
    SystemProducer Producer         { get; }
    ConsumeFilter  Filter           { get; }
    StreamId       SnapshotStreamId { get; }
    TimeProvider   Time             { get; }

    /// <summary>
    /// Asynchronously retrieves an array of active connectors registered in the system.
    /// </summary>
    /// <param name="cancellationToken">A CancellationToken to observe while waiting for the task to complete.</param>
    public async Task<RegisteredConnector[]> GetConnectors(CancellationToken cancellationToken) {
        var (state, checkpoint) = await LoadSnapshot(cancellationToken);

        var lastReadPosition = checkpoint;

        var records = Reader.ReadForwards(
            checkpoint.LogPosition,
            Filter,
            cancellationToken: cancellationToken
        );

        await foreach (var record in records) {
            switch (record.Value) {
                case ConnectorActivating activating:
                    state.Add(
                        activating.ConnectorId,
                        new RegisteredConnector(
                            activating.ConnectorId,
                            activating.Revision,
                            new ConnectorSettings(activating.Settings.ToDictionary())
                        )
                    );
                    break;

                case ConnectorDeactivating deactivating:
                    state.Remove(deactivating.ConnectorId);
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

        async Task<(Dictionary<ConnectorId, RegisteredConnector> State, RecordPosition SnapshotPosition)> LoadSnapshot(CancellationToken ct) {
            var record = await Reader
                .ReadBackwards(ConsumeFilter.Streams(SnapshotStreamId), 1, ct)
                .FirstOrNullAsync(ct) ?? EventStoreRecord.None;

            // if (record.Value is not RegisteredConnectors snapshot)
            //     return ([], record.Position with { LogPosition = LogPosition.Earliest }); // this is nicer... need to get it back.

            if (record.Value is not RegisteredConnectors snapshot)
                return ([], RecordPosition.ForStream(record.Position.StreamId, record.Position.StreamRevision, LogPosition.Earliest));

            var snapshotState = snapshot.Connectors.ToDictionary(
                x => ConnectorId.From(x.ConnectorId),
                x => new RegisteredConnector(
                    x.ConnectorId,
                    x.Revision,
                    new ConnectorSettings(x.Settings.ToDictionary())
                )
            );

            return (snapshotState, record.Position);
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
                UpdatedAt   = Time.GetUtcNow().ToTimestamp()
            };

            await Producer.Produce(
                ProduceRequest.Builder
                    .Message(newSnapshot)
                    .Stream(SnapshotStreamId)
                    .ExpectedStreamRevision(expectedPosition.StreamRevision)
                    .Create()
            );
        }
    }
}