// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Globalization;

namespace KurrentDB.Core.Data;

public readonly struct TFPos : IEquatable<TFPos>, IComparable<TFPos> {
	public static readonly TFPos Invalid = new TFPos(-1, -1);
	public static readonly TFPos HeadOfTf = new TFPos(-1, -1);
	public static readonly TFPos FirstRecordOfTf = new TFPos(0, 0);

	public readonly long CommitPosition;
	public readonly long PreparePosition;

	public TFPos(long commitPosition, long preparePosition) {
		CommitPosition = commitPosition;
		PreparePosition = preparePosition;
	}

	[System.Diagnostics.Contracts.Pure]
	public string AsString() {
		return $"{CommitPosition:X16}{PreparePosition:X16}";
	}

	public static bool TryParse(string s, out TFPos pos) {
		pos = Invalid;
		if (s is not { Length: 32 })
			return false;

		if (!long.TryParse(s.AsSpan(0, 16), NumberStyles.HexNumber, null, out var commitPos))
			return false;
		if (!long.TryParse(s.AsSpan(16, 16), NumberStyles.HexNumber, null, out var preparePos))
			return false;
		pos = new TFPos(commitPos, preparePos);
		return true;
	}

	public int CompareTo(TFPos other) {
		if (CommitPosition < other.CommitPosition)
			return -1;
		if (CommitPosition > other.CommitPosition)
			return 1;
		return PreparePosition.CompareTo(other.PreparePosition);
	}

	public bool Equals(TFPos other) {
		return this == other;
	}

	public override bool Equals(object obj) {
		if (ReferenceEquals(null, obj))
			return false;
		return obj is TFPos pos && Equals(pos);
	}

	public override int GetHashCode() {
		unchecked {
			return (CommitPosition.GetHashCode() * 397) ^ PreparePosition.GetHashCode();
		}
	}

	public static bool operator ==(TFPos left, TFPos right) {
		return left.CommitPosition == right.CommitPosition && left.PreparePosition == right.PreparePosition;
	}

	public static bool operator !=(TFPos left, TFPos right) {
		return !(left == right);
	}

	public static bool operator <=(TFPos left, TFPos right) {
		return !(left > right);
	}

	public static bool operator >=(TFPos left, TFPos right) {
		return !(left < right);
	}

	public static bool operator <(TFPos left, TFPos right) {
		return left.CommitPosition < right.CommitPosition
			   || (left.CommitPosition == right.CommitPosition && left.PreparePosition < right.PreparePosition);
	}

	public static bool operator >(TFPos left, TFPos right) {
		return left.CommitPosition > right.CommitPosition
			   || (left.CommitPosition == right.CommitPosition && left.PreparePosition > right.PreparePosition);
	}

	public override string ToString() {
		return $"C:{CommitPosition}/P:{PreparePosition}";
	}
}
