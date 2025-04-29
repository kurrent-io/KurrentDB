// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DuckDB.NET.Native;

namespace Kurrent.Quack;

partial struct StreamQueryResult {
	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_execute_prepared_streaming")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState ExecutePrepared(nint preparedStatement, out DuckDBResult result);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_row_count")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial long GetRowsCount(in DuckDBResult result);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_column_count")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial long GetsColumnCount(in DuckDBResult result);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_stream_fetch_chunk")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial nint Fetch(DuckDBResult result);
}
