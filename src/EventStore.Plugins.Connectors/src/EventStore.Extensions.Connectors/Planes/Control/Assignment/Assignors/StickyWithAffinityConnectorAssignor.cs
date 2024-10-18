using EventStore.Connect.Connectors;
using EventStore.Streaming;
using DistributionTable = System.Collections.Generic.Dictionary<EventStore.Connectors.Control.ClusterNodeId, int>;

namespace EventStore.Connectors.Control.Assignment.Assignors;

public class StickyWithAffinityConnectorAssignor : AffinityConnectorAssignorBase {
    const int MaximumLoadThresholdPercentile = 10;

    public override ConnectorAssignmentStrategy Type => ConnectorAssignmentStrategy.StickyWithAffinity;

    protected override IEnumerable<(ConnectorId ConnectorId, ClusterNodeId NodeId)> AssignConnectors(
        ClusterNode[] clusterNodes,
        ConnectorResource[] connectors,
        ClusterConnectorsAssignment currentClusterAssignment
    ) {
        DistributionTable distributionTable = clusterNodes.ToDictionary(
            x => x.NodeId,
            x => currentClusterAssignment.TryGetAssignment(x.NodeId, out var assigned) ? assigned.Count : 0
        );

        var assignments = connectors.Select(
            connector => AssignConnector(
                connector.ConnectorId,
                distributionTable,
                connectorId => GetRoundRobinNode(connectorId, clusterNodes),
                currentClusterAssignment.GetAssignedNode
            )
        );

        return assignments;

        static ClusterNodeId GetRoundRobinNode(ConnectorId connectorId, IReadOnlyList<ClusterNode> nodes) {
            var index = (int)(HashGenerators.FromString.MurmurHash3(connectorId) % nodes.Count);
            return nodes[index].NodeId;
        }
    }

    /// <summary>
    /// Uses a percentile based load threshold (still based on a simple count)
    /// to decide if a connector should be moved to a different node
    /// </summary>
    static (ConnectorId ConnectorId, ClusterNodeId NodeId) AssignConnector(
        ConnectorId connectorId,
        DistributionTable distributionTable,
        Func<ConnectorId, ClusterNodeId> getNodeByRoundRobin,
        Func<ConnectorId, ClusterNodeId> getAssignedNode
    ) {
        var mostLoadedNode  = distributionTable.MaxBy(x => x.Value).Key;
        var leastLoadedNode = distributionTable.MinBy(x => x.Value).Key;
        var assignedNode    = getAssignedNode(connectorId);
        var calculatedNode  = getNodeByRoundRobin(connectorId);

        // var calculatedNodeLoadDifference        = distributionTable[mostLoadedNode] - distributionTable[calculatedNode];
        // var calculatedNodeLoadThresholdExceeded = calculatedNodeLoadDifference > MaximumLoadThreshold;

        var calculatedNodeLoadThresholdExceeded = false;
        if (distributionTable[mostLoadedNode] != 0 && distributionTable[calculatedNode] != 0) {
            var calculatedNodeLoadPercentile =
                distributionTable[calculatedNode] * 100 / distributionTable[mostLoadedNode];

            calculatedNodeLoadThresholdExceeded = calculatedNodeLoadPercentile > MaximumLoadThresholdPercentile;
        }

        // ---------------------------------------------------------------------------------------------------
        // in case the connector is already assigned to a node, we check if the calculated node is different
        // then we must consider the load threshold before we decide to keep the assigned node or
        // move it to the calculated node or least loaded node
        // if the connector is not assigned to any node, we just assign it to the calculated node
        // ---------------------------------------------------------------------------------------------------

        // no previous assigment, we just assign the connector to the calculated
        // node by default unless the load threshold is exceeded
        if (assignedNode == ClusterNodeId.None) {
            if (calculatedNodeLoadThresholdExceeded) {
                distributionTable[leastLoadedNode]++;
                return new(connectorId, leastLoadedNode);
            }

            distributionTable[calculatedNode]++;
            return new(connectorId, calculatedNode);
        }

        // if the connector is already assigned to a node,
        // we check if the calculated node is the same
        if (assignedNode != calculatedNode) {
            // if the calculated node load threshold is exceeded, we keep
            // the assigned node as there is no reason to move it
            if (calculatedNodeLoadThresholdExceeded)
                // nothing to count here, we just keep the assigned node
                return new(connectorId, assignedNode);

            // too much load lets reassign the connector to
            // calculated node not the least loaded node
            distributionTable[assignedNode]--;
            distributionTable[calculatedNode]++;
            return new(connectorId, calculatedNode);
        }

        // if the calculated node is the same as the assigned node, we check if the load
        // threshold is exceeded, if so we move the connector to the least loaded node
        // if not we just keep the assigned node
        if (!calculatedNodeLoadThresholdExceeded)
            // nothing to count here, we just keep the assigned node
            return new(connectorId, assignedNode);

        distributionTable[calculatedNode]--; // or assignedNode
        distributionTable[leastLoadedNode]++;
        return new(connectorId, leastLoadedNode);
    }
}