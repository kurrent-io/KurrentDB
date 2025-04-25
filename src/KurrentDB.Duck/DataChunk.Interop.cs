// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DuckDB.NET.Native;

namespace KurrentDB.Duck;

partial struct DataChunk {
	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_data_chunk_get_column_count")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial long GetColumnCount(nint chunk);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_data_chunk_get_size")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial long GetRowsCount(nint chunk);
}
