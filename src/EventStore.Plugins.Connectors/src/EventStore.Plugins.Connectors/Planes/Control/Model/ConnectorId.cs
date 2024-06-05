namespace EventStore.Connectors.Control;

public readonly record struct ConnectorId(Guid NodeId) : IComparable<ConnectorId>, IComparable {
	public static readonly ConnectorId None = new(Guid.Empty);
	
	public override string ToString() => NodeId.ToString();

	#region . equality members .
	
	public bool Equals(ConnectorId other) => NodeId.Equals(other.NodeId);

	public override int GetHashCode() => NodeId.GetHashCode();

	#endregion . equality members .
	
	#region . relational members .
	
	public int CompareTo(ConnectorId other) => 
		string.Compare(NodeId.ToString(), other.NodeId.ToString(), StringComparison.Ordinal);

	public int CompareTo(object? obj) {
		if (ReferenceEquals(null, obj))
			return 1;

		return obj is ConnectorId other
			? CompareTo(other)
			: throw new ArgumentException($"Object must be of type {nameof(ConnectorId)}");
	}

	public static bool operator <(ConnectorId left, ConnectorId right) => left.CompareTo(right) < 0;
	public static bool operator >(ConnectorId left, ConnectorId right) => left.CompareTo(right) > 0;
	public static bool operator <=(ConnectorId left, ConnectorId right) => left.CompareTo(right) <= 0;
	public static bool operator >=(ConnectorId left, ConnectorId right) => left.CompareTo(right) >= 0;

	#endregion
	
	public static implicit operator Guid(ConnectorId _)   => _.NodeId;
	public static implicit operator ConnectorId(Guid _)   => new(_);
	public static implicit operator string(ConnectorId _) => _.ToString();
	public static implicit operator ConnectorId(string _) => new(Guid.Parse(_));
	
	public static ConnectorId From(Guid value)   => new(value);
	public static ConnectorId From(string value) => new(Guid.Parse(value));
}