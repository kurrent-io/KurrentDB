namespace EventStore.Connectors.Control;

/// <summary>
///  Represents a node in the cluster.
///  The node is identified by its <see cref="NodeId"/>.
/// </summary>
public record ClusterNode : IComparable<ClusterNode>, IComparable {
	public static readonly ClusterNode Unmapped = new(ClusterNodeId.None, ClusterNodeState.Unmapped);

    // static readonly DnsEndPoint NoEndpoint = new("0.0.0.0", 0);

    public ClusterNode() { }

    public ClusterNode(ClusterNodeId nodeId, ClusterNodeState state) {
        NodeId = nodeId;
        State  = state;
    }

    public ClusterNodeId    NodeId              { get; init; }
    public ClusterNodeState State               { get; init; }
    // public DnsEndPoint      HttpEndpoint        { get; init; } = NoEndpoint;
    // public DnsEndPoint      InternalTcpEndpoint { get; init; } = NoEndpoint;

    public bool HasVanished => State == ClusterNodeState.Unmapped;

    public override string ToString() => NodeId.ToString();

    #region . equality members .
	public virtual bool Equals(ClusterNode? other) {
		if (ReferenceEquals(null, other))
			return false;

		if (ReferenceEquals(this, other))
			return true;

		return NodeId.Equals(other.NodeId);
	}

	public override int GetHashCode() => NodeId.GetHashCode();

	#endregion . equality members .

	#region . relational members .

	public int CompareTo(ClusterNode? other) {
		if (ReferenceEquals(this, other))
			return 0;

		if (ReferenceEquals(null, other))
			return 1;

		var stateComparison = State.CompareTo(other.State);
		return stateComparison != 0
			? stateComparison
			: NodeId.CompareTo(other.NodeId);
	}

	public int CompareTo(object? obj) {
		if (ReferenceEquals(null, obj))
			return 1;

		if (ReferenceEquals(this, obj))
			return 0;

		return obj is ClusterNode other
			? CompareTo(other)
			: throw new ArgumentException($"Object must be of type {nameof(ClusterNode)}");
	}

	public static bool operator <(ClusterNode? left, ClusterNode? right) => Comparer<ClusterNode>.Default.Compare(left, right) < 0;

	public static bool operator >(ClusterNode? left, ClusterNode? right) => Comparer<ClusterNode>.Default.Compare(left, right) > 0;

	public static bool operator <=(ClusterNode? left, ClusterNode? right) => Comparer<ClusterNode>.Default.Compare(left, right) <= 0;

	public static bool operator >=(ClusterNode? left, ClusterNode? right) => Comparer<ClusterNode>.Default.Compare(left, right) >= 0;

	#endregion . relational members .

    public static implicit operator ClusterNodeId(ClusterNode _) => _.NodeId;

    public void Deconstruct(out ClusterNodeId nodeId, out ClusterNodeState state) {
        nodeId = NodeId;
        state  = State;
    }
}