// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
using DotNext.Buffers;
using DuckDB.NET.Data;
using DuckDB.NET.Native;

namespace KurrentDB.Duck;

internal static class Interop {
	public const string LibraryName = "duckdb";

	private static unsafe string FromUnmanagedUtf8String(nint unmanagedUtf8String) {
		ReadOnlySpan<byte> utf8Chars = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)unmanagedUtf8String);
		return Encoding.UTF8.GetString(utf8Chars);
	}

	[UnsafeAccessor(UnsafeAccessorKind.Constructor)]
	public static extern DuckDBException CreateException(string message, DuckDBErrorType errorType = DuckDBErrorType.UnknownType);

	public static DuckDBException CreateException(nint unmanagedUtf8String,
		DuckDBErrorType errorType = DuckDBErrorType.UnknownType)
		=> CreateException(FromUnmanagedUtf8String(unmanagedUtf8String), errorType);

	public static string ToUtf16String(ReadOnlySpan<byte> utf8Chars) {
		var maxChars = Encoding.UTF8.GetMaxCharCount(utf8Chars.Length);

		using var buffer = maxChars <= SpanOwner<char>.StackallocThreshold
			? stackalloc char[maxChars]
			: new SpanOwner<char>(maxChars);

		return Utf8.ToUtf16(utf8Chars, buffer.Span, out _, out var charsWritten, replaceInvalidSequences: false) is
			OperationStatus.Done
			? new(buffer.Span.Slice(0, charsWritten))
			: throw CreateException($"Unable to convert BLOB to UTF-16");
	}
}
