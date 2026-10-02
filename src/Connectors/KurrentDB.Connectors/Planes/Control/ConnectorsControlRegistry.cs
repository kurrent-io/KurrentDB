// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using KurrentDB.Connectors.Control.Contracts;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge;
using Kurrent.Surge.Connectors;
using Kurrent.Surge.Consumers;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Readers;

using KurrentDB.Connectors.Planes.Control.Model;
using KurrentDB.Surge.Producers;
using KurrentDB.Surge.Readers;
using KurrentDB.Connectors.Planes.Management;
using ConnectorSettings = System.Collections.Generic.IDictionary<string, string?>;

namespace KurrentDB.Connectors.Planes.Control;

public record ConnectorsControlRegistryOptions {
    public StreamId SnapshotStreamId { get; init; }
}

class ConnectorsControlRegistry {
    public ConnectorsControlRegistry(
	    IStartupWorkCompletionMonitor startupWorkMonitor,
        ConnectorsControlRegistryOptions options,
        Func<SystemReaderBuilder> getReaderBuilder,
        Func<SystemProducerBuilder> getProducerBuilder,
        TimeProvider time
    ) {
	    StartupWorkMonitor = startupWorkMonitor;
        Options  = options;
        Reader   = getReaderBuilder().ReaderId("ConnectorsControlRegistryReader").Create();
        Producer = getProducerBuilder().ProducerId("ConnectorsControlRegistryProducer").Create();
        Time     = time;
    }

    IStartupWorkCompletionMonitor StartupWorkMonitor { get; }
    ConnectorsControlRegistryOptions Options  { get; }
    SystemReader                     Reader   { get; }
    SystemProducer                   Producer { get; }
    TimeProvider                     Time     { get; }

    public async Task<ActiveConnectors> LoadSnapshot(CancellationToken cancellationToken) {
	    await StartupWorkMonitor.WhenCompletedAsync();

        try {
            var snapshotRecord = await Reader.ReadLastStreamRecord(Options.SnapshotStreamId, cancellationToken);

            if (snapshotRecord.Value is ActivatedConnectorsSnapshot snapshot) {
                var state = snapshot.Connectors.ToDictionary(
                    conn => ConnectorId.From(conn.ConnectorId),
                    conn => new RegisteredConnector(conn.ConnectorId, conn.Revision, conn.Settings)
                );

                return new(state, snapshot.LogPosition);
            }

            var head = await Reader
                .ReadBackwards(ConsumeFilter.None, maxCount: 1, cancellationToken: cancellationToken)
                .FirstOrDefaultAsync(cancellationToken);

            var connectors = new ActiveConnectors([], head.Position);

            await SaveSnapshot(connectors);

            return connectors;
        }
        catch (Exception ex) {
            throw new Exception("Failed to load activated connectors snapshot", ex);
        }
    }

    public async Task SaveSnapshot(ActiveConnectors connectors) {
        try {
            var snapshot = new ActivatedConnectorsSnapshot {
                Connectors  = { connectors.Select(MapToConnector) },
                LogPosition = connectors.Position.LogPosition.CommitPosition!.Value,
                TakenAt     = Time.GetUtcNow().ToTimestamp()
            };

            var request = ProduceRequest.Builder
                .Message(snapshot)
                .Stream(Options.SnapshotStreamId)
                .ExpectedStreamState(StreamState.Any)
                .Create();

            await Producer.Produce(request);
        }
        catch (Exception ex) {
            throw new Exception("Failed to update activated connectors snapshot", ex);
        }

        return;

        static ActivatedConnectorsSnapshot.Types.Connector MapToConnector(RegisteredConnector source) =>
            new() {
                ConnectorId = source.ConnectorId,
                Revision    = source.Revision,
                Settings    = { source.Settings }
            };
    }
}

public record RegisteredConnector(ConnectorId ConnectorId, int Revision, ConnectorSettings Settings) {
    public ConnectorResource Resource     { get; } = new(ConnectorId, Settings.NodeAffinity());
    public ClusterNodeState  NodeAffinity { get; } = Settings.NodeAffinity();
}

public delegate Task<ActiveConnectors> LoadActiveConnectorsSnapshot(CancellationToken cancellationToken);

public delegate Task SaveActiveConnectorsSnapshot(ActiveConnectors connectors);
