// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DuckDB.NET.Native;

namespace KurrentDB.Duck;

partial struct PreparedStatement {
	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_prepare")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Create(nint connection, in byte queryUtf8NullTerminated, out nint preparedStatement);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_prepare_error")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial nint GetErrorString(nint preparedStatement);

	static nint INativeWrapper<PreparedStatement>.GetErrorString(nint preparedStatement) =>
		GetErrorString(preparedStatement);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_destroy_prepare")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial void Destroy(in nint preparedStatement);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_clear_bindings")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState ClearBindings(nint preparedStatement);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_nparams")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial long GetParametersCount(nint preparedStatement);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_bind_null")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState BindNull(nint preparedStatement, long index);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_bind_int32")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Bind(nint preparedStatement, long index, int value);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_bind_uint32")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Bind(nint preparedStatement, long index, uint value);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_bind_int64")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Bind(nint preparedStatement, long index, long value);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_bind_uint64")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Bind(nint preparedStatement, long index, ulong value);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_bind_timestamp")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState BindTimestamp(nint preparedStatement, long index, long microseconds);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_bind_blob")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState BindBlob(nint preparedStatement, long index, in byte data, long length);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_bind_varchar_length")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState BindVarChar(nint preparedStatement, long index, in byte utf8String, long length);
}
