// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using DuckDB.NET.Data;
using DuckDB.NET.Native;

namespace KurrentDB.Duck;

/// <summary>
/// Represents reusable appender.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly partial struct Appender : INativeWrapper<Appender> {
	private readonly nint _appender;

	public Appender(DuckDBNativeConnection connection, ReadOnlySpan<byte> tableNameUtf8) {
		if (Create(connection.DangerousGetHandle(), 0, in tableNameUtf8.GetPinnableReference(), out _appender) is
		    DuckDBState.Error) {
			var ex = Interop.CreateException(GetErrorString(_appender));
			Destroy(in _appender);
			_appender = 0;
			throw ex;
		}
	}

	public Appender(DuckDBConnection connection, ReadOnlySpan<byte> tableNameUtf8)
		: this(connection.NativeConnection, tableNameUtf8) {
	}

	/// <summary>
	/// Flushes the appender.
	/// </summary>
	public void Flush() => VerifyState(Flush(_appender) is DuckDBState.Error);

	[StackTraceHidden]
	private void VerifyState([DoesNotReturnIf(true)] bool isError)
		=> INativeWrapper<Appender>.VerifyState(isError, _appender);

	public void Dispose() {
		if (_appender is not 0 && Destroy(in _appender) is DuckDBState.Error)
			throw Interop.CreateException("Unable to destroy appender.");
	}
}
