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
using DotNext.Buffers.Binary;
using DuckDB.NET.Data;
using DuckDB.NET.Native;

namespace KurrentDB.Duck;

/// <summary>
/// Represents prepared statement.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly partial struct PreparedStatement : INativeWrapper<PreparedStatement> {
	private readonly nint _preparedStatement;

	public PreparedStatement(DuckDBNativeConnection connection, ReadOnlySpan<byte> commandTextUtf8) {
		if (Create(connection.DangerousGetHandle(), in commandTextUtf8.GetPinnableReference(),
			    out _preparedStatement) is DuckDBState.Error) {
			var ex = Interop.CreateException(GetErrorString(_preparedStatement));
			Destroy(in _preparedStatement);
			_preparedStatement = 0;
			throw ex;
		}
	}

	public PreparedStatement(DuckDBConnection connection, ReadOnlySpan<byte> commandTextUtf8)
		: this(connection.NativeConnection, commandTextUtf8) {
	}

	/// <summary>
	/// Gets the number of formal parameters declared in this prepared statement.
	/// </summary>
	public long ParametersCount => _preparedStatement is not 0 ? GetParametersCount(_preparedStatement) : 0;

	[StackTraceHidden]
	private void VerifyState([DoesNotReturnIf(true)] bool isError)
		=> INativeWrapper<PreparedStatement>.VerifyState(isError, _preparedStatement);

	public void ClearBindings()
		=> VerifyState(ClearBindings(_preparedStatement) is DuckDBState.Error);

	private long ToNativeIndex(Index index) => index.IsFromEnd
		? index.Value - GetParametersCount(_preparedStatement)
		: index.Value;

	public void Bind(Index index, DBNull value) {
		ArgumentNullException.ThrowIfNull(value);

		VerifyState(BindNull(_preparedStatement, ToNativeIndex(index)) is DuckDBState.Error);
	}

	public void Bind(Index index, int value)
		=> VerifyState(Bind(_preparedStatement, ToNativeIndex(index), value) is DuckDBState.Error);

	public void Bind(Index index, uint value)
		=> VerifyState(Bind(_preparedStatement, ToNativeIndex(index), value) is DuckDBState.Error);

	public void Bind(Index index, long value)
		=> VerifyState(Bind(_preparedStatement, ToNativeIndex(index), value) is DuckDBState.Error);

	public void Bind(Index index, ulong value)
		=> VerifyState(Bind(_preparedStatement, ToNativeIndex(index), value) is DuckDBState.Error);

	public void Bind(Index index, DateTime value) {
		var timestamp = NativeMethods.DateTimeHelpers.DuckDBToTimestamp(DuckDBTimestamp.FromDateTime(value));
		VerifyState(BindTimestamp(_preparedStatement, ToNativeIndex(index), timestamp.Micros) is DuckDBState.Error);
	}

	public void Bind(Index index, ReadOnlySpan<byte> buffer) => VerifyState(
		BindBlob(_preparedStatement, ToNativeIndex(index), in buffer.GetPinnableReference(), buffer.Length) is
			DuckDBState.Error);

	[SkipLocalsInit]
	public void Bind<T>(Index index, T value)
		where T : struct, IBinaryFormattable<T> {
		using var buffer = (uint)T.Size <= (uint)SpanOwner<byte>.StackallocThreshold
			? stackalloc byte[T.Size]
			: new SpanOwner<byte>(T.Size);

		value.Format(buffer.Span);
		Bind(index, buffer.Span);
	}

	[SkipLocalsInit]
	public void Bind(Index index, ReadOnlySpan<char> chars) {
		var byteCount = Encoding.UTF8.GetMaxByteCount(chars.Length);
		using var buffer = (uint)byteCount <= (uint)SpanOwner<byte>.StackallocThreshold
			? stackalloc byte[byteCount]
			: new SpanOwner<byte>(byteCount);

		if (Utf8.FromUtf16(chars, buffer.Span, out _, out var bytesWritten, replaceInvalidSequences: false) is
		    OperationStatus.Done) {
			VerifyState(
				BindVarChar(_preparedStatement, ToNativeIndex(index), in buffer.GetPinnableReference(),
					bytesWritten) is DuckDBState.Error);
		} else {
			throw Interop.CreateException($"Unable to convert string {chars} to UTF-8");
		}
	}

	public long ExecuteNonQuery() {
		long count;
		if (_preparedStatement is not 0) {
			using var query = new QueryResult(_preparedStatement);
			count = query.RowsChanged;
		} else {
			count = 0L;
		}

		return count;
	}

	public StreamQueryResult ExecuteQuery()
		=> _preparedStatement is not 0 ? new(_preparedStatement) : default;

	public void Dispose() {
		if (_preparedStatement is not 0)
			Destroy(in _preparedStatement);
	}
}
