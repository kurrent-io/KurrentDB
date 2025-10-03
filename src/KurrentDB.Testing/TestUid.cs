// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KurrentDB.Testing;

/// <summary>
/// Generates short unique identifiers for tests to be able to
/// correlate log, tracing or other info to a specific test run.
/// <remarks>
/// The generated 48-bit ID is a hexadecimal string derived from a folded GUID,
/// ensuring uniqueness while keeping the identifier concise.
/// </remarks>
/// </summary>
[PublicAPI]
public readonly record struct TestUid() {
    const string EmptyValue = "000000000000";

    static readonly SearchValues<char> HexChars =
		SearchValues.Create("0123456789ABCDEFabcdef");

	/// <summary>
	/// Represents an empty TestUid
	/// </summary>
	public static readonly TestUid Empty = new();

	/// <summary>
	/// The unique identifier value as a 12-character hexadecimal string.
	/// This value is immutable and set during initialization.
	/// </summary>
	public string Value { get; private init; } = EmptyValue;

	/// <inheritdoc/>
	public override string ToString() => Value;

	/// <inheritdoc/>
	public bool Equals(TestUid other) => Value == other.Value;

	/// <inheritdoc/>
	public override int GetHashCode() => Value.GetHashCode();

    public static implicit operator string(TestUid _) => _.Value;
    public static implicit operator TestUid(string _) => Parse(_);

	/// <summary>
	/// Attempts to parse a string into a TestUid.
	/// </summary>
	public static bool TryParse(string? input, out TestUid uid) {
		if (input?.Length == 12 && !input.ContainsAnyExcept(HexChars)) {
			uid = input;
			return true;
		}

		uid = Empty;
		return false;
	}

	/// <summary>
	/// Parses a string into a TestUid, throwing a FormatException if invalid.
	/// </summary>
	public static TestUid Parse(string? input) =>
		!TryParse(input, out var result)
			? throw new FormatException($"'{input}' is not a valid TestUid")
			: result;

	/// <summary>
	/// Generate a 12-char hex ID (48-bit)
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe TestUid New() {
		Span<byte> bytes = stackalloc byte[16];

		Guid.NewGuid().TryWriteBytes(bytes);

		var folded = MemoryMarshal.Read<ulong>(bytes)
                   ^ MemoryMarshal.Read<ulong>(bytes[8..]);

		return new() {
            Value = (folded & 0xFFFFFFFFFFFF).ToString("X12")
        };
	}
}
