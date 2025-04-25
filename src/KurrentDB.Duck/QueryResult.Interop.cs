// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DuckDB.NET.Native;

namespace KurrentDB.Duck;

partial struct QueryResult {
	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_execute_prepared")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState ExecutePrepared(nint preparedStatement, out DuckDBResult result);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_result_error")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial nint GetErrorString(in DuckDBResult result);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_result_error_type")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial DuckDBErrorType GetErrorType(in DuckDBResult result);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_destroy_result")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	internal static partial void Destroy(in DuckDBResult result);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_rows_changed")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial long GetRowsChangedCount(in DuckDBResult result);
}
