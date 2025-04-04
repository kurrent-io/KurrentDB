// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Services.Transport.Common;

/// <summary>
/// A structure referring to a potential logical record position
/// in the Event Store transaction file.
/// </summary>
public readonly struct Position : IEquatable<Position>, IComparable<Position> {
	/// <summary>
	/// Position representing the start of the transaction file
	/// </summary>
	public static readonly Position Start = new Position(0, 0);

	/// <summary>
	/// Position representing the end of the transaction file
	/// </summary>
	public static readonly Position End = new Position(ulong.MaxValue, ulong.MaxValue);

	public static Position FromInt64(long commitPosition, long preparePosition)
		=> new Position(
			commitPosition == -1 ? ulong.MaxValue : (ulong)commitPosition,
			preparePosition == -1 ? ulong.MaxValue : (ulong)preparePosition);

	/// <summary>
	/// The commit position of the record
	/// </summary>
	public readonly ulong CommitPosition;

	/// <summary>
	/// The prepare position of the record.
	/// </summary>
	public readonly ulong PreparePosition;

	/// <summary>
	/// Constructs a position with the given commit and prepare positions.
	/// It is not guaranteed that the position is actually the start of a
	/// record in the transaction file.
	///
	/// The commit position cannot be less than the prepare position.
	/// </summary>
	/// <param name="commitPosition">The commit position of the record.</param>
	/// <param name="preparePosition">The prepare position of the record.</param>
	public Position(ulong commitPosition, ulong preparePosition) {
		if (commitPosition < preparePosition)
			throw new ArgumentOutOfRangeException(
				nameof(commitPosition),
				"The commit position cannot be less than the prepare position");

		if (commitPosition > long.MaxValue && commitPosition != ulong.MaxValue) {
			throw new ArgumentOutOfRangeException(nameof(commitPosition));
		}


		if (preparePosition > long.MaxValue && preparePosition != ulong.MaxValue) {
			throw new ArgumentOutOfRangeException(nameof(preparePosition));
		}

		CommitPosition = commitPosition;
		PreparePosition = preparePosition;
	}

	/// <summary>
	/// Compares whether p1 &lt; p2.
	/// </summary>
	/// <param name="p1">A <see cref="Position" />.</param>
	/// <param name="p2">A <see cref="Position" />.</param>
	/// <returns>True if p1 &lt; p2.</returns>
	public static bool operator <(Position p1, Position p2) =>
		p1.CommitPosition < p2.CommitPosition ||
		p1.CommitPosition == p2.CommitPosition && p1.PreparePosition < p2.PreparePosition;


	/// <summary>
	/// Compares whether p1 &gt; p2.
	/// </summary>
	/// <param name="p1">A <see cref="Position" />.</param>
	/// <param name="p2">A <see cref="Position" />.</param>
	/// <returns>True if p1 &gt; p2.</returns>
	public static bool operator >(Position p1, Position p2) =>
		p1.CommitPosition > p2.CommitPosition ||
		p1.CommitPosition == p2.CommitPosition && p1.PreparePosition > p2.PreparePosition;

	/// <summary>
	/// Compares whether p1 &gt;= p2.
	/// </summary>
	/// <param name="p1">A <see cref="Position" />.</param>
	/// <param name="p2">A <see cref="Position" />.</param>
	/// <returns>True if p1 &gt;= p2.</returns>
	public static bool operator >=(Position p1, Position p2) => p1 > p2 || p1 == p2;

	/// <summary>
	/// Compares whether p1 &lt;= p2.
	/// </summary>
	/// <param name="p1">A <see cref="Position" />.</param>
	/// <param name="p2">A <see cref="Position" />.</param>
	/// <returns>True if p1 &lt;= p2.</returns>
	public static bool operator <=(Position p1, Position p2) => p1 < p2 || p1 == p2;

	/// <summary>
	/// Compares p1 and p2 for equality.
	/// </summary>
	/// <param name="p1">A <see cref="Position" />.</param>
	/// <param name="p2">A <see cref="Position" />.</param>
	/// <returns>True if p1 is equal to p2.</returns>
	public static bool operator ==(Position p1, Position p2) =>
		Equals(p1, p2);

	/// <summary>
	/// Compares p1 and p2 for equality.
	/// </summary>
	/// <param name="p1">A <see cref="Position" />.</param>
	/// <param name="p2">A <see cref="Position" />.</param>
	/// <returns>True if p1 is not equal to p2.</returns>
	public static bool operator !=(Position p1, Position p2) => !(p1 == p2);

	///<inheritdoc cref="IComparable{T}.CompareTo"/>
	public int CompareTo(Position other) => this == other ? 0 : this > other ? 1 : -1;

	/// <summary>
	/// Indicates whether this instance and a specified object are equal.
	/// </summary>
	/// <returns>
	/// true if <paramref name="obj"/> and this instance are the same type and represent the same value; otherwise, false.
	/// </returns>
	/// <param name="obj">Another object to compare to. </param><filterpriority>2</filterpriority>
	public override bool Equals(object obj) => obj is Position position && Equals(position);

	/// <summary>
	/// Compares this instance of <see cref="Position" /> for equality
	/// with another instance.
	/// </summary>
	/// <param name="other">A <see cref="Position" /></param>
	/// <returns>True if this instance is equal to the other instance.</returns>
	public readonly bool Equals(Position other) =>
		CommitPosition == other.CommitPosition && PreparePosition == other.PreparePosition;

	/// <summary>
	/// Returns the hash code for this instance.
	/// </summary>
	/// <returns>
	/// A 32-bit signed integer that is the hash code for this instance.
	/// </returns>
	/// <filterpriority>2</filterpriority>
	public override int GetHashCode() => HashCode.Hash.Combine(CommitPosition).Combine(PreparePosition);

	/// <summary>
	/// Returns the fully qualified type name of this instance.
	/// </summary>
	/// <returns>
	/// A <see cref="T:System.String"/> containing a fully qualified type name.
	/// </returns>
	/// <filterpriority>2</filterpriority>
	public override string ToString() => this == End ? "End" : $"C:{CommitPosition}/P:{PreparePosition}";

	public readonly (long commitPosition, long preparePosition) ToInt64() => Equals(End)
		? (-1, -1)
		: ((long)CommitPosition, (long)PreparePosition);
}
