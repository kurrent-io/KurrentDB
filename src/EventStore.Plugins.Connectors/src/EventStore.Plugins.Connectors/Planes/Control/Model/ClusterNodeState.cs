namespace EventStore.Connectors.Control;

/// <summary>
/// Represents the state of a cluster node in the EventStore.
/// </summary>
public enum ClusterNodeState {
    /// <summary>
    /// The node is not mapped to any role.
    /// </summary>
    Unmapped = 0,

    /// <summary>
    /// The node is the leader of the cluster.
    /// </summary>
    Leader = 1,

    /// <summary>
    /// The node is a follower in the cluster.
    /// </summary>
    Follower = 2,

    /// <summary>
    /// The node is a read-only replica in the cluster.
    /// </summary>
    ReadOnlyReplica = 3,
}