namespace EventStore.Connectors.Control;

public readonly record struct ClusterNodeId(Guid Value) : IComparable<ClusterNodeId>, IComparable {
	public static readonly ClusterNodeId None = new(Guid.Empty);

	public override string ToString() => Value.ToString();

	#region . equality members .

	public bool Equals(ClusterNodeId other) => Value.Equals(other.Value);

	public override int GetHashCode() => Value.GetHashCode();

	#endregion . equality members .

	#region . relational members .

	public int CompareTo(ClusterNodeId other) =>
		string.Compare(Value.ToString(), other.Value.ToString(), StringComparison.Ordinal);

	public int CompareTo(object? obj) {
		if (ReferenceEquals(null, obj))
			return 1;

		return obj is ClusterNodeId other
			? CompareTo(other)
			: throw new ArgumentException($"Object must be of type {nameof(ClusterNodeId)}");
	}

	public static bool operator <(ClusterNodeId left, ClusterNodeId right)  => left.CompareTo(right) < 0;
	public static bool operator >(ClusterNodeId left, ClusterNodeId right)  => left.CompareTo(right) > 0;
	public static bool operator <=(ClusterNodeId left, ClusterNodeId right) => left.CompareTo(right) <= 0;
	public static bool operator >=(ClusterNodeId left, ClusterNodeId right) => left.CompareTo(right) >= 0;

	#endregion

	public static implicit operator Guid(ClusterNodeId _)   => _.Value;
	public static implicit operator ClusterNodeId(Guid _)   => new(_);
	public static implicit operator string(ClusterNodeId _) => _.ToString();
	public static implicit operator ClusterNodeId(string _) => new(Guid.Parse(_));

	public static ClusterNodeId From(Guid value)   => new(value);
    public static ClusterNodeId From(string value) => new(Guid.Parse(value));
}