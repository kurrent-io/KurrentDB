// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.Contracts;

namespace EventStore.Core.Data;

public struct TFPos : IEquatable<TFPos>, IComparable<TFPos> {
	public static readonly TFPos Invalid = new TFPos(-1, -1);
	public static readonly TFPos HeadOfTf = new TFPos(-1, -1);

	public readonly long CommitPosition;
	public readonly long PreparePosition;

	public TFPos(long commitPosition, long preparePosition) {
		CommitPosition = commitPosition;
		PreparePosition = preparePosition;
	}

	[Pure]
	public string AsString() {
		return string.Format("{0:X16}{1:X16}", CommitPosition, PreparePosition);
	}

	public static bool TryParse(string s, out TFPos pos) {
		pos = Invalid;
		if (s == null || s.Length != 32)
			return false;

		long commitPos;
		long preparePos;
		if (!long.TryParse(s.Substring(0, 16), System.Globalization.NumberStyles.HexNumber, null, out commitPos))
			return false;
		if (!long.TryParse(s.Substring(16, 16), System.Globalization.NumberStyles.HexNumber, null, out preparePos))
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
		if (ReferenceEquals(null, obj)) return false;
		return obj is TFPos && Equals((TFPos)obj);
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
		return string.Format("C:{0}/P:{1}", CommitPosition, PreparePosition);
	}
}
