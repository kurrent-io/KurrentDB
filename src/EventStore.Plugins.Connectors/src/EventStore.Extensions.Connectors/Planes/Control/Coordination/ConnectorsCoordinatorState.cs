using System.Collections.Concurrent;
using DotNext.Collections.Generic;
using EventStore.Connect.Connectors;
using EventStore.Connectors.Control.Assignment;
using EventStore.Streaming;

namespace EventStore.Connectors.Control.Coordination;

class ConnectorsCoordinatorState {
    public ClusterTopology                                        CurrentTopology { get; set; } = ClusterTopology.Unknown;
    public ConcurrentDictionary<ConnectorId, RegisteredConnector> Connectors      { get; set; } = [];

    public ClusterConnectorsAssignment CurrentAssignment { get; private set; }
    public ConnectorTransfers          PendingTransfers  { get; private set; } = new();

    public void SetNewAssignment(ClusterConnectorsAssignment assignment) {
        CurrentAssignment = assignment;
        PendingTransfers  = ConnectorTransfers.FromGroupAssignment(assignment);
    }

    internal class ConnectorTransfers {
        public static ConnectorTransfers FromGroupAssignment(ClusterConnectorsAssignment assignment) {
            ConcurrentDictionary<ConnectorId, ConnectorTransfer> transfers = [];

            // add all connectors that need to be activated regardless of the
            // fact that they might be reassigned to another node
            transfers.AddAll(
                assignment
                    .SelectMany(x => x.Value.Assigned.Select(connectorId => new ConnectorTransfer(connectorId, ClusterNodeId.None, x.Key)))
                    .ToDictionary(x => x.ConnectorId)
            );

            // update transfers with connectors that are being reassigned
            // add transfers for connectors that are being deactivated and not reassigned
            assignment
                .SelectMany(x => x.Value.Revoked.Select(connectorId => new ConnectorTransfer(connectorId, x.Key, ClusterNodeId.None)))
                .ForEach(
                    transfer => {
                        transfers.AddOrUpdate(
                            transfer.ConnectorId,
                            static (_, transfer) => transfer,
                            static (_, connectors, transfer) => connectors with { From = transfer.From },
                            transfer
                        );
                    }
                );

            return new() {
                Transfers = transfers,
            };
        }

        ConcurrentDictionary<ConnectorId, ConnectorTransfer> Transfers { get; init; } = [];

        /// <summary>
        /// Returns all connectors that are to be deactivated regardless of the fact
        /// that they might be reassigned to another node.
        /// </summary>
        public IReadOnlyDictionary<ClusterNodeId, List<ConnectorId>> GetRevokedConnectorsByCluster() {
            ConcurrentDictionary<ClusterNodeId, List<ConnectorId>> result = [];

            foreach (var (connectorId, transfer) in Transfers.Where(x => x.Value.From != ClusterNodeId.None)) {
                result.AddOrUpdate(
                    transfer.From,
                    static (_, connectorId) => [connectorId],
                    static (_, connectors, connectorId) => connectors.With(x => x.Add(connectorId)),
                    connectorId
                );
            }

            return result;
        }

        /// <summary>
        /// Returns all connectors that are to be activated for the first time.
        /// </summary>
        public IReadOnlyDictionary<ClusterNodeId, List<ConnectorId>> GetNewlyAssignedConnectorsByCluster() {
            ConcurrentDictionary<ClusterNodeId, List<ConnectorId>> result = [];

            foreach (var (connectorId, transfer) in Transfers.Where(x => x.Value.From == ClusterNodeId.None)) {
                result.AddOrUpdate(
                    transfer.To,
                    static (_, connectorId) => [connectorId],
                    static (_, connectors, connectorId) => connectors.With(x => x.Add(connectorId)),
                    connectorId
                );
            }

            return result;
        }

        public IReadOnlyList<ConnectorTransfer> Complete(params ConnectorId[] connectors) {
            List<ConnectorTransfer> completed = [];

            connectors.ForEach(
                x => {
                    if (Transfers.Remove(x, out var transfer))
                        completed.Add(transfer);
                }
            );

            return completed;
        }

        public ConcurrentDictionary<ClusterNodeId, List<ConnectorId>> GetAssignedNodesByCluster(params ConnectorId[] connectors) {
            ConcurrentDictionary<ClusterNodeId, List<ConnectorId>> result = [];

            foreach (var (connectorId, transfer) in Transfers.Where(x => connectors.Contains(x.Key))) {
                result.AddOrUpdate(
                    transfer.To, // can be ClusterNodeId.None and that is nice
                    static (_, connectorId) => [connectorId],
                    static (_, connectors, connectorId) => connectors.With(x => x.Add(connectorId)),
                    connectorId
                );
            }

            return result;
        }

        public ConcurrentDictionary<ClusterNodeId, List<ConnectorId>> GetAssignedNodesByCluster(IEnumerable<ConnectorId> connectors) =>
            GetAssignedNodesByCluster(connectors.ToArray());

        public int Count => Transfers.Count;

        public record ConnectorTransfer(ConnectorId ConnectorId, ClusterNodeId From, ClusterNodeId To);
    }
}