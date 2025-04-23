// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNext.Runtime;
using DuckDB.NET.Native;

namespace KurrentDB.Duck;

internal partial struct QueryResult {
	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_execute_prepared")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState ExecutePrepared(nint preparedStatement, out byte result);

	private static DuckDBState ExecutePrepared(nint preparedStatement, out DuckDBResult result) {
		Unsafe.SkipInit(out result);
		return ExecutePrepared(preparedStatement, out Unsafe.As<DuckDBResult, byte>(ref result));
	}

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_result_error")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial nint GetErrorString(in byte result);

	private static nint GetErrorString(ref readonly DuckDBResult result)
		=> GetErrorString(in Unsafe.As<DuckDBResult, byte>(ref Unsafe.AsRef(in result)));

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_result_error_type")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBErrorType GetErrorType(in byte result);

	private static DuckDBErrorType GetErrorType(ref readonly DuckDBResult result)
		=> GetErrorType(in Unsafe.As<DuckDBResult, byte>(ref Unsafe.AsRef(in result)));

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_destroy_result")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial void Destroy(in byte result);

	private static void Destroy(ref readonly DuckDBResult result)
		=> Destroy(in Unsafe.As<DuckDBResult, byte>(ref Unsafe.AsRef(in result)));

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_rows_changed")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial long GetRowsChangedCount(in byte result);

	private static long GetRowsChangedCount(ref readonly DuckDBResult result)
		=> GetRowsChangedCount(in Unsafe.As<DuckDBResult, byte>(ref Unsafe.AsRef(in result)));
}
