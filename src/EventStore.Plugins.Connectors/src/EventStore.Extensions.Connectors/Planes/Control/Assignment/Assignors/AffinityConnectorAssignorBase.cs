using EventStore.Connect.Connectors;

namespace EventStore.Connectors.Control.Assignment.Assignors;

/// <summary>
/// Base class for connector assignment strategies that take into account the affinity of the connector app.
/// </summary>
public abstract class AffinityConnectorAssignorBase : IConnectorAssignor {
    public abstract ConnectorAssignmentStrategy Type { get; }

    public ClusterConnectorsAssignment Assign(
        ClusterTopology topology,
        IEnumerable<ConnectorResource> connectors,
        ClusterConnectorsAssignment? currentClusterAssignment = null
    ) {
        currentClusterAssignment ??= new();

        // first we group all connectors by their affinity and only then do we try to assign them to the nodes
        // if a connector has no affinity (Unmapped), it will be assigned to the first lower state node
        // Unmapped -> ReadOnlyReplica -> Follower -> Leader

        var emptyClusterAssignment = new ClusterConnectorsAssignment(
            Guid.NewGuid(),
            topology.Nodes.ToDictionary(x => x.NodeId, _ => new NodeConnectorsAssignment([]))
        );

        var newClusterAssignment = connectors
            .GroupBy(connector => connector.Affinity)
            .Aggregate(emptyClusterAssignment, (result, grp) => {
                var affinity = grp.Key == ClusterNodeState.Unmapped
                    ? ClusterNodeState.ReadOnlyReplica
                    : grp.Key;

                return topology.TryGetNodesByAffinity(affinity, out var nodes)
                    ? result.AddNodeAssignments(AssignConnectors(nodes, grp.ToArray(), currentClusterAssignment))
                    : result;
            });

        // calculate the delta between the current and the new cluster assignment
        return currentClusterAssignment.Delta(newClusterAssignment);
    }

    protected abstract IEnumerable<(ConnectorId ConnectorId, ClusterNodeId NodeId)> AssignConnectors(
        ClusterNode[] clusterNodes,
        ConnectorResource[] connectors,
        ClusterConnectorsAssignment currentClusterAssignment
    );
}