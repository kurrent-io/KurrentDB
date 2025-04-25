// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
using DotNext.Buffers;
using DuckDB.NET.Native;

namespace KurrentDB.Duck;

partial struct Appender {
	/// <summary>
	/// Prepares a fresh row for insertion.
	/// </summary>
	/// <returns></returns>
	public Row CreateRow() => new(_appender);

	[StructLayout(LayoutKind.Auto)]
	public readonly struct Row : IDisposable {
		private readonly nint _appender;

		internal Row(nint appender) => _appender = appender;

		public void Append(int value)
			=> VerifyState(Appender.Append(_appender, value) is DuckDBState.Error);

		public void Append(uint value)
			=> VerifyState(Appender.Append(_appender, value) is DuckDBState.Error);

		public void Append(long value)
			=> VerifyState(Appender.Append(_appender, value) is DuckDBState.Error);

		public void Append(ulong value)
			=> VerifyState(Appender.Append(_appender, value) is DuckDBState.Error);

		public void Append(DBNull value) {
			ArgumentNullException.ThrowIfNull(value);

			VerifyState(AppendNull(_appender) is DuckDBState.Error);
		}

		public void AppendDefault()
			=> VerifyState(Appender.AppendDefault(_appender) is DuckDBState.Error);

		public void Append(DateTime value) {
			var timestamp = NativeMethods.DateTimeHelpers.DuckDBToTimestamp(DuckDBTimestamp.FromDateTime(value));
			VerifyState(AppendTimestamp(_appender, timestamp.Micros) is DuckDBState.Error);
		}

		[SkipLocalsInit]
		public void Append(ReadOnlySpan<char> chars) {
			var byteCount = Encoding.UTF8.GetMaxByteCount(chars.Length);
			using var buffer = (uint)byteCount <= (uint)SpanOwner<byte>.StackallocThreshold
				? stackalloc byte[byteCount]
				: new SpanOwner<byte>(byteCount);

			if (Utf8.FromUtf16(chars, buffer.Span, out _, out var bytesWritten, replaceInvalidSequences: false) is
			    OperationStatus.Done) {
				VerifyState(
					AppendVarChar(_appender, in buffer.GetPinnableReference(), bytesWritten) is DuckDBState.Error);
			} else {
				throw Interop.CreateException($"Unable to convert string {chars} to UTF-8");
			}
		}

		public void Append(ReadOnlySpan<byte> bytes) {
			VerifyState(AppendBlob(_appender, in bytes.GetPinnableReference(), bytes.Length) is DuckDBState.Error);
		}

		[StackTraceHidden]
		private void VerifyState([DoesNotReturnIf(true)] bool isError)
			=> INativeWrapper<Appender>.VerifyState(isError, _appender);

		public void Dispose()
			=> VerifyState(EndRow(_appender) is DuckDBState.Error);
	}
}
