using EventStore.Connect.Connectors;
using DistributionTable = System.Collections.Generic.Dictionary<EventStore.Connectors.Control.ClusterNodeId, int>;

namespace EventStore.Connectors.Control.Assignment.Assignors;

public class LeastLoadedWithAffinityConnectorAssignor : AffinityConnectorAssignorBase {
    public override ConnectorAssignmentStrategy Type => ConnectorAssignmentStrategy.LeastLoadedWithAffinity;

    protected override IEnumerable<(ConnectorId ConnectorId, ClusterNodeId NodeId)> AssignConnectors(
        ClusterNode[] clusterNodes,
        ConnectorResource[] connectors,
        ClusterConnectorsAssignment currentClusterAssignment
    ) {
        DistributionTable distributionTable = clusterNodes.ToDictionary(
            x => x.NodeId,
            x => currentClusterAssignment.TryGetAssignment(x.NodeId, out var assigned) ? assigned.Count : 0
        );

        var assignments = connectors.Select(x => AssignConnector(x.ConnectorId, distributionTable)).ToList();

        return assignments;

        static (ConnectorId ConnectorId, ClusterNodeId NodeId) AssignConnector(ConnectorId connectorId, DistributionTable distributionTable) {
            var leastLoadedNode = distributionTable.MinBy(x => x.Value).Key;
            distributionTable[leastLoadedNode]++;
            return (connectorId, leastLoadedNode);
        }
    }
}