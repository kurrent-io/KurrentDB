// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Connectors.Planes.Control.Model;

/// <summary>
/// Represents a connector in the cluster.
/// The connector resource is identified by its <see cref="ConnectorId"/>.
/// </summary>
public record ConnectorResource(Kurrent.Surge.Connectors.ConnectorId ConnectorId, ClusterNodeState Affinity) : IComparable<ConnectorResource>, IComparable {
	public static readonly ConnectorResource Unmapped = new(Kurrent.Surge.Connectors.ConnectorId.None, ClusterNodeState.Unmapped);

	public override string ToString() => ConnectorId.ToString();

	#region . equality members .
	public virtual bool Equals(ConnectorResource? other) {
		if (ReferenceEquals(null, other))
			return false;

		if (ReferenceEquals(this, other))
			return true;

		return ConnectorId.Equals(other.ConnectorId);
	}

	public override int GetHashCode() => ConnectorId.GetHashCode();

	#endregion . relational members .

	#region . relational members .
	public int CompareTo(ConnectorResource? other) {
		if (ReferenceEquals(this, other))
			return 0;

		if (ReferenceEquals(null, other))
			return 1;

		var affinityComparison = Affinity.CompareTo(other.Affinity);
		if (affinityComparison != 0)
			return affinityComparison;

		return ConnectorId.CompareTo(other.ConnectorId);
	}

	public int CompareTo(object? obj) {
		if (ReferenceEquals(null, obj))
			return 1;

		if (ReferenceEquals(this, obj))
			return 0;

		return obj is ConnectorResource other
			? CompareTo(other)
			: throw new ArgumentException($"Object must be of type {nameof(ConnectorResource)}");
	}

	public static bool operator <(ConnectorResource? left, ConnectorResource? right)  => Comparer<ConnectorResource>.Default.Compare(left, right) < 0;
	public static bool operator >(ConnectorResource? left, ConnectorResource? right)  => Comparer<ConnectorResource>.Default.Compare(left, right) > 0;
	public static bool operator <=(ConnectorResource? left, ConnectorResource? right) => Comparer<ConnectorResource>.Default.Compare(left, right) <= 0;
	public static bool operator >=(ConnectorResource? left, ConnectorResource? right) => Comparer<ConnectorResource>.Default.Compare(left, right) >= 0;

	#endregion . relational members .

    public static implicit operator Kurrent.Surge.Connectors.ConnectorId(ConnectorResource _) => _.ConnectorId;
}