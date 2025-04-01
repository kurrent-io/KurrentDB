namespace EventStore.Connectors.Control.Assignment;

/// <summary>
/// Interface for defining a strategy for assigning connectors to nodes in a cluster.
/// </summary>
public interface IConnectorAssignor {
    /// <summary>
    /// Gets the type of the connector assignment strategy.
    /// </summary>
    public ConnectorAssignmentStrategy Type { get; }

    /// <summary>
    /// Assigns connectors to nodes in the cluster based on the topology and the connectors.
    /// </summary>
    /// <param name="topology">The topology of the cluster.</param>
    /// <param name="connectors">The connectors to be assigned.</param>
    /// <param name="currentClusterAssignment">The current assignment of connectors to nodes. This is used to determine the current state of the cluster.</param>
    /// <returns>A dictionary mapping node IDs to the result of the assignment for each node.</returns>
    ClusterConnectorsAssignment Assign(
        ClusterTopology topology, 
        IEnumerable<ConnectorResource> connectors, 
        ClusterConnectorsAssignment? currentClusterAssignment = null
    );
}