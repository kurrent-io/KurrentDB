// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Buffers;
using DuckDB.NET.Native;

namespace Kurrent.Quack;

public static unsafe class DuckDBStringExtensions {
	public static ReadOnlySpan<byte> AsSpan(this in DuckDBString str)
		=> new(str.Data, str.Length);

	public static ReadOnlyMemory<byte> AsMemory(this in DuckDBString str)
		=> UnmanagedMemory.AsMemory((byte*)str.Data, str.Length);

	public static string ToUtf16String(this in DuckDBString str)
		=> Interop.ToUtf16String(str.AsSpan());
}
