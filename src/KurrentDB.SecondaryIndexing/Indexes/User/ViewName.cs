// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

[StructLayout(LayoutKind.Auto)]
internal readonly struct ViewName : IEquatable<ViewName> {
	private readonly byte[] _encodedValue;
	private readonly int _hash; // cached for better performance

	public ViewName(ReadOnlySpan<char> value) {
		var encoding = Encoding;
		var length = encoding.GetByteCount(value);
		encoding.GetBytes(value, _encodedValue = new byte[length]);
		_hash = GetHashCode(_encodedValue);
	}

	private static Encoding Encoding => Encoding.UTF8;

	public ViewName(ReadOnlySpan<byte> encodedValue) {
		_encodedValue = encodedValue.ToArray();
		_hash = GetHashCode(_encodedValue);
	}

	public static int GetHashCode(ReadOnlySpan<byte> value) {
		var hash = new HashCode();
		hash.AddBytes(value);
		return hash.ToHashCode();
	}

	public ReadOnlySpan<byte> Value => _encodedValue;

	public bool Equals(ViewName other) => _encodedValue.SequenceEqual(other._encodedValue);

	public override bool Equals([NotNullWhen(true)] object? other)
		=> other is ViewName name && Equals(name);

	public override int GetHashCode() => _hash;

	public override string ToString() => Encoding.GetString(_encodedValue);
}
