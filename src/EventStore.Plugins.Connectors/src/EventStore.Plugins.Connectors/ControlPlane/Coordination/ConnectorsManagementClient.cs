#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using System.Collections.Concurrent;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Streaming;
using EventStore.Streaming.Consumers;
using EventStore.Streaming.Readers;

namespace EventStore.Connectors.ControlPlane.Coordination;

public delegate Task<RegisteredConnector[]> ListActiveConnectors(CancellationToken cancellationToken);

class ConnectorsManagementClient(SystemReader reader, ConsumeFilter filter) {
    public ConnectorsManagementClient(SystemReader reader) : this(reader, ConsumeFilter.Streams("$connector-")) { }
    
    SystemReader  Reader { get; } = reader;
    ConsumeFilter Filter { get; } = filter;

    public async Task<RegisteredConnector[]> ListActiveConnectors(CancellationToken cancellationToken) {
        // we basically re-hidrate the state of the connectors to ensure we have the latest state
        // this is a very simple implementation, we can improve it by using a snapshot of the state
        
        ConcurrentDictionary<string, RegisteredConnector> result = new();

        await foreach (var record in Reader.ReadForwards(Filter, cancellationToken: cancellationToken)) {
            switch (record.Value) {
                case ConnectorActivating evt:
                    var state = (
                        Revision: evt.Revision,
                        Settings: new ConnectorSettings(evt.Settings.ToDictionary()),
                        Timestamp: evt.Timestamp.ToDateTimeOffset(),
                        LogPosition: record.LogPosition
                    );
                    
                    result.AddOrUpdate(
                        evt.ConnectorId,
                        static (connectorId, state) => new(
                            connectorId, 
                            state.Revision,
                            state.Settings,
                            state.LogPosition, 
                            state.Timestamp
                        ),
                        static (_, existing, state) => existing with {
                            Revision  = state.Revision,
                            Settings  = state.Settings,
                            Position  = state.LogPosition,
                            Timestamp = state.Timestamp
                        },
                        state
                    );

                    break;
                
                case ConnectorDeactivating evt:
                    result.Remove(evt.ConnectorId, out _);
                    break;
            }
        }

        return result.Values.ToArray();
    }
}

public record RegisteredConnector(ConnectorId ConnectorId, int Revision, ConnectorSettings Settings, LogPosition Position, DateTimeOffset Timestamp) {
    public ConnectorResource Resource     { get; } = new(ConnectorId, Settings.NodeAffinity);
    public ClusterNodeState  NodeAffinity { get; } = Settings.NodeAffinity;
}