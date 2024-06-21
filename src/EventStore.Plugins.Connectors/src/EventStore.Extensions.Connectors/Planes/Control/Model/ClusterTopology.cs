using System.Diagnostics.CodeAnalysis;

namespace EventStore.Connectors.Control;

/// <summary>
/// Represents the topology of a cluster, including its nodes and their states.
/// </summary>
[PublicAPI]
public class ClusterTopology : IEquatable<ClusterTopology> {
	public static readonly ClusterTopology Unknown = new([]);

    public ClusterTopology(List<ClusterNode> nodes) {
		Nodes = new SortedSet<ClusterNode>(nodes.Distinct());

		NodesById    = Nodes.ToDictionary(x => x.NodeId);
		NodesByState = Nodes.GroupBy(x => x.State).ToDictionary(x => x.Key, x => x.ToArray());
		NodesByIndex = Nodes.Select((node, idx) => (idx, node)).ToDictionary(x => x.idx, x => x.node);
	}

	public IReadOnlySet<ClusterNode>                            Nodes        { get; }
	public IReadOnlyDictionary<ClusterNodeId, ClusterNode>      NodesById    { get; }
	public IReadOnlyDictionary<ClusterNodeState, ClusterNode[]> NodesByState { get; }
	public IReadOnlyDictionary<int, ClusterNode>                NodesByIndex { get; }

	public ClusterNode   this[ClusterNodeId _]    => NodesById[_];
	public ClusterNode[] this[ClusterNodeState _] => NodesByState[_];
	public ClusterNode   this[int _]              => NodesByIndex[_];

    public bool IsUnknown => this == Unknown;

    /// <summary>
    /// Tries to get the nodes in the cluster that have the specified affinity, falling back to the next higher state if the nodes are not found.
    /// </summary>
    /// <remarks>
    /// The order of states from low to high is: ReadOnlyReplica, Follower, Leader, Unmapped.
    /// If the specified affinity is Unmapped, this method starts with ReadOnlyReplica.
    /// </remarks>
    public bool TryGetNodesByAffinity(ClusterNodeState affinity, [MaybeNullWhen(false)] out ClusterNode[] nodes) {
        var nextState = affinity == ClusterNodeState.Unmapped
            ? ClusterNodeState.ReadOnlyReplica
            : affinity;

        do {
            if (NodesByState.TryGetValue(nextState, out nodes))
                return true;

            nextState = (ClusterNodeState)nextState.GetHashCode() - 1;
        } while (nextState != ClusterNodeState.Unmapped);

        return false;
    }

    public ClusterNode[] GetNodesByAffinity(ClusterNodeState affinity) =>
        TryGetNodesByAffinity(affinity, out var nodes) ? nodes : [];

	public static ClusterTopology From(List<ClusterNode> nodes) => new(nodes);

	public static ClusterTopology From(IEnumerable<ClusterNode> nodes) => new(nodes.ToList());

    public static ClusterTopology From(params ClusterNode[] nodes) => new(nodes.ToList());

	#region .  equality members  .

	public bool Equals(ClusterTopology? other) {
		if (ReferenceEquals(null, other))
			return false;

		if (ReferenceEquals(this, other))
			return true;

		if (Nodes.Count != other.Nodes.Count)
			return false;

		var membersStateChanged = !other.Nodes.Any(
			node => NodesById.TryGetValue(node.NodeId, out var existingMember)
			     && existingMember.State != node.State
		);

		return membersStateChanged;
	}

	public override bool Equals(object? obj) {
		if (ReferenceEquals(null, obj))
			return false;

		if (ReferenceEquals(this, obj))
			return true;

		if (obj.GetType() != GetType())
			return false;

		return Equals((ClusterTopology)obj);
	}

	public override int GetHashCode() => Nodes.GetHashCode();

	public static bool operator ==(ClusterTopology? left, ClusterTopology? right) => Equals(left, right);

	public static bool operator !=(ClusterTopology? left, ClusterTopology? right) => !Equals(left, right);

	#endregion
}