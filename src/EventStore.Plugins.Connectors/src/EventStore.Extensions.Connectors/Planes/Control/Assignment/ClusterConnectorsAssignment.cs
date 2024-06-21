using System.Collections;
using System.Diagnostics.CodeAnalysis;
using EventStore.Connect.Connectors;

namespace EventStore.Connectors.Control.Assignment;

[PublicAPI]
public record ClusterConnectorsAssignment : IReadOnlyDictionary<ClusterNodeId, NodeConnectorsAssignment> {
    public ClusterConnectorsAssignment() {
        AssignmentId = Guid.NewGuid();
        Assignments  = new();
    }

    public ClusterConnectorsAssignment(Guid assignmentId, Dictionary<ClusterNodeId, NodeConnectorsAssignment> assignments) {
        AssignmentId = assignmentId;
        Assignments  = assignments;
    }

    Dictionary<ClusterNodeId, NodeConnectorsAssignment> Assignments { get; set; }

    public Guid AssignmentId { get; }

    public ClusterConnectorsAssignment AddNodeAssignments(IEnumerable<(ConnectorId ConnectorId, ClusterNodeId NodeId)> assignments) {
        Dictionary<ClusterNodeId, NodeConnectorsAssignment> currentClusterAssignment = new(Assignments);

        foreach (var assignmentsByNode in assignments.GroupBy(x => x.NodeId)) {
            var nodeId            = assignmentsByNode.Key;
            var newNodeAssignment = assignmentsByNode.Select(x => x.ConnectorId);

            if (currentClusterAssignment.TryGetValue(nodeId, out var currentNodeAssignment))
                currentClusterAssignment[nodeId] = NodeConnectorsAssignment.From(newNodeAssignment.Concat(currentNodeAssignment));
            else
                currentClusterAssignment[nodeId] = NodeConnectorsAssignment.From(newNodeAssignment);
        }

        return this with {
            Assignments = currentClusterAssignment
        };
    }

    public ClusterConnectorsAssignment Delta(ClusterConnectorsAssignment newClusterAssignment) {
        Dictionary<ClusterNodeId, NodeConnectorsAssignment> currentClusterAssignment = new(Assignments);

        foreach (var (nodeId, newNodeAssignment) in newClusterAssignment) {
            if (currentClusterAssignment.TryGetValue(nodeId, out var currentNodeAssignment))
                currentClusterAssignment[nodeId] = currentNodeAssignment.Apply(newNodeAssignment);
            else
                currentClusterAssignment[nodeId] = newNodeAssignment;
        }

        return this with {
            Assignments = currentClusterAssignment
        };
    }

    /// <summary>
    /// Tries to get the assignment for the specified node.
    /// </summary>
    /// <param name="nodeId">The ID of the node for which to get the assignment.</param>
    /// <param name="assignment">When this method returns, contains the <see cref="NodeConnectorsAssignment"/> for the specified node, if found; otherwise, null.</param>
    /// <returns>true if an assignment for the specified node is found; otherwise, false.</returns>
    public bool TryGetAssignment(ClusterNodeId nodeId, [MaybeNullWhen(false)] out NodeConnectorsAssignment assignment) =>
        Assignments.TryGetValue(nodeId, out assignment);

    /// <summary>
    /// Gets the node to which the specified connector is assigned.
    /// </summary>
    /// <param name="connectorId">The ID of the connector for which to get the assigned node.</param>
    /// <returns>The ID of the node to which the connector is assigned.</returns>
    public ClusterNodeId GetAssignedNode(ConnectorId connectorId) =>
        Assignments.FirstOrDefault(x => x.Value.Any(c => c == connectorId)).Key;

    #region . IReadOnlyDictionary .

    public IEnumerator<KeyValuePair<ClusterNodeId, NodeConnectorsAssignment>> GetEnumerator() =>
        Assignments.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => Assignments.Count;

    public bool ContainsKey(ClusterNodeId key) =>
        Assignments.ContainsKey(key);

    public bool TryGetValue(ClusterNodeId key, [MaybeNullWhen(false)] out NodeConnectorsAssignment value) =>
        Assignments.TryGetValue(key, out value);

    public NodeConnectorsAssignment this[ClusterNodeId key] => Assignments[key];

    public IEnumerable<ClusterNodeId>            Keys   => Assignments.Keys;
    public IEnumerable<NodeConnectorsAssignment> Values => Assignments.Values;

    #endregion
}