// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
using DuckDB.NET.Data;

namespace KurrentDB.Duck;

internal static class Interop {
	public const string LibraryName = "duckdb";

	private static unsafe string FromUnmanagedUtf8String(nint unmanagedUtf8String) {
		ReadOnlySpan<byte> utf8Chars = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)unmanagedUtf8String);
		return Encoding.UTF8.GetString(utf8Chars);
	}

	[UnsafeAccessor(UnsafeAccessorKind.Constructor)]
	public static extern DuckDBException CreateException(string message);

	public static DuckDBException CreateException(nint unmanagedUtf8String)
		=> CreateException(FromUnmanagedUtf8String(unmanagedUtf8String));
}
