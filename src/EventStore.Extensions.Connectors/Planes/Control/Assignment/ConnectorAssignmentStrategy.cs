namespace EventStore.Connectors.Control.Assignment;

/// <summary>
/// Represents the strategy used to assign <see cref="ConnectorResource"/>'s to <see cref="ClusterNode"/>'s
/// </summary>
public enum ConnectorAssignmentStrategy {
	/// <summary>
	/// This is the default value.
	/// </summary>
	Unspecified = 0,

	/// <summary>
	/// This strategy assigns connectors to nodes in a round-robin fashion, taking into account the affinity of the connector app.
	/// </summary>
	RoundRobinWithAffinity = 1,

	/// <summary>
	/// This strategy assigns connectors to the least loaded nodes, taking into account the affinity of the connector app.
	/// </summary>
	LeastLoadedWithAffinity = 2,

	/// <summary>
	/// This strategy assigns connectors to the same node as long as it is available, taking into account the affinity of the connector app.
	/// </summary>
	StickyWithAffinity = 3
}