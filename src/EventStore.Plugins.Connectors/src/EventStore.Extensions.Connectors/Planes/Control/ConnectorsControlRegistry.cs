#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using System.Collections;
using EventStore.Connect.Connectors;
using EventStore.Connect.Producers;
using EventStore.Connect.Producers.Configuration;
using EventStore.Connect.Readers;
using EventStore.Connect.Readers.Configuration;
using EventStore.Connectors.Control.Contracts;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Readers;
using EventStore.Toolkit;
using Google.Protobuf.WellKnownTypes;
using ConnectorSettings = System.Collections.Generic.IDictionary<string, string?>;

namespace EventStore.Connectors.Control;

public record ConnectorsControlRegistryOptions {
    public ConsumeFilter Filter           { get; init; }
    public StreamId      SnapshotStreamId { get; init; }
}

class ConnectorsControlRegistry {
    public ConnectorsControlRegistry(
        ConnectorsControlRegistryOptions options,
        Func<SystemReaderBuilder> getReaderBuilder,
        Func<SystemProducerBuilder> getProducerBuilder,
        TimeProvider time
    ) {
        Options  = options;
        Reader   = getReaderBuilder().ReaderId("ConnectorsControlRegistryReader").Create();
        Producer = getProducerBuilder().ProducerId("ConnectorsControlRegistryProducer").Create();
        Time     = time;
    }

    ConnectorsControlRegistryOptions Options  { get; }
    SystemReader                     Reader   { get; }
    SystemProducer                   Producer { get; }
    TimeProvider                     Time     { get; }

    /// <summary>
    /// Asynchronously retrieves an array of active connectors registered in the system.
    /// </summary>
    /// <param name="cancellationToken">A CancellationToken to observe while waiting for the task to complete.</param>
    public async Task<GetConnectorsResult> GetConnectors(CancellationToken cancellationToken) {
        var (state, checkpoint, snapshotPosition) = await LoadSnapshot(cancellationToken);

        var lastReadPosition = checkpoint;

        var records = Reader.ReadForwards(checkpoint.LogPosition, Options.Filter, cancellationToken: cancellationToken);

        const string startPositionKey = "Subscription:StartPosition";

        await foreach (var record in records) {
            switch (record.Value) {
                case ConnectorActivating activating:
                    // hijack settings and inject the start position
                    if (activating.StartFrom is not null)
                        activating.Settings[startPositionKey] = activating.StartFrom.LogPosition.ToString();

                    state[activating.ConnectorId] = new RegisteredConnector(
                        activating.ConnectorId,
                        activating.Revision,
                        activating.Settings
                    );
                    break;

                case ConnectorRunning running:
                    // remove the start position from the settings in case one was set
                    var connector = state[running.ConnectorId];
                    state[running.ConnectorId] = connector with {
                        Settings = connector.Settings.With(x => x.Remove(startPositionKey))
                    };
                    break;

                case ConnectorDeactivating deactivating:
                    state.Remove(deactivating.ConnectorId);
                    break;
            }

            lastReadPosition = record.Position;
        }

        var result = state.Values.ToList();

        // updates the snapshot every time the last record position is newer,
        // regardless of state changes
        if (lastReadPosition != checkpoint)
            await UpdateSnapshot(result, lastReadPosition, snapshotPosition);

        return new GetConnectorsResult {
            Connectors = result,
            Position   = lastReadPosition
        };

        async Task<(Dictionary<ConnectorId, RegisteredConnector> State, RecordPosition Checkpoint, RecordPosition SnapshotPosition)> LoadSnapshot(CancellationToken ct) {
            try {
                var snapshotRecord = await Reader.ReadLastStreamRecord(Options.SnapshotStreamId, ct);

                if (snapshotRecord.Value is not ActivatedConnectorsSnapshot snapshot)
                    return ([], RecordPosition.Earliest, snapshotRecord.Position);

                var snapshotState = snapshot.Connectors.ToDictionary(
                    conn => ConnectorId.From(conn.ConnectorId),
                    conn => new RegisteredConnector(conn.ConnectorId, conn.Revision, conn.Settings)
                );

                return (snapshotState, snapshot.LogPosition, snapshotRecord.Position);
            }
            catch (Exception ex) {
                throw new Exception("Failed to load activated connectors snapshot", ex);
            }
        }

        async Task UpdateSnapshot(List<RegisteredConnector> connectors, RecordPosition newCheckpoint, RecordPosition expectedPosition) {
            try {
                var newSnapshot = MapToSnapshot(connectors, newCheckpoint, Time.GetUtcNow());

                var requestBuilder = ProduceRequest.Builder
                    .Message(newSnapshot)
                    .Stream(Options.SnapshotStreamId)
                    .ExpectedStreamRevision(expectedPosition.StreamRevision);

                await Producer.Produce(requestBuilder.Create());
            }
            catch (Exception ex) {
                throw new Exception("Failed to update activated connectors snapshot", ex);
            }

            return;

            static ActivatedConnectorsSnapshot MapToSnapshot(List<RegisteredConnector> connectors, RecordPosition position, DateTimeOffset now) {
                return new ActivatedConnectorsSnapshot {
                    Connectors  = { connectors.Select(MapToConnector) },
                    LogPosition = position.LogPosition.CommitPosition!.Value,
                    TakenAt     = now.ToTimestamp()
                };

                ActivatedConnectorsSnapshot.Types.Connector MapToConnector(RegisteredConnector source) =>
                    new() {
                        ConnectorId = source.ConnectorId,
                        Revision    = source.Revision,
                        Settings    = { source.Settings }
                    };
            }
        }
    }
}

public record GetConnectorsResult : IEnumerable<RegisteredConnector> {
    public List<RegisteredConnector> Connectors { get; init; } = [];
    public RecordPosition            Position   { get; init; } = RecordPosition.Earliest;

    public IEnumerator<RegisteredConnector> GetEnumerator() => Connectors.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Deconstruct(out List<RegisteredConnector> connectors, out RecordPosition position) {
        connectors = Connectors;
        position   = Position;
    }
}

public record RegisteredConnector(ConnectorId ConnectorId, int Revision, ConnectorSettings Settings) {
    public ConnectorResource Resource     { get; } = new(ConnectorId, Settings.NodeAffinity());
    public ClusterNodeState  NodeAffinity { get; } = Settings.NodeAffinity();
}

public delegate Task<GetConnectorsResult> GetActiveConnectors(CancellationToken cancellationToken);