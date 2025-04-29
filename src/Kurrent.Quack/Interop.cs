// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
using DotNext.Buffers;
using DuckDB.NET.Data;
using DuckDB.NET.Native;

namespace Kurrent.Quack;

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

	[SkipLocalsInit]
	public static bool TryToUtf16String(ReadOnlySpan<byte> utf8Chars, [NotNullWhen(true)] out string? result) {
		var maxChars = Encoding.UTF8.GetMaxCharCount(utf8Chars.Length);

		if (maxChars > 0) {
			using var buffer = maxChars <= SpanOwner<char>.StackallocThreshold
				? stackalloc char[maxChars]
				: new SpanOwner<char>(maxChars);

			if (Utf8.ToUtf16(utf8Chars, buffer.Span, out _, out var charsWritten, replaceInvalidSequences: false) is
			    OperationStatus.Done) {
				result = new(buffer.Span.Slice(0, charsWritten));
				return true;
			}
		}

		result = null;
		return false;
	}

	public static string ToUtf16String(ReadOnlySpan<byte> utf8Chars) => TryToUtf16String(utf8Chars, out var result)
		? result
		: throw CreateException($"Unable to convert BLOB to UTF-16");
}
