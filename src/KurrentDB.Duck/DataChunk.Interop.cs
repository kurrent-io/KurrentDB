// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KurrentDB.Duck;

partial struct DataChunk {
	[DllImport(Interop.LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
		EntryPoint = "duckdb_data_chunk_get_column_count")]
	[SuppressGCTransition]
	private static extern long GetColumnCount(nint chunk);

	[DllImport(Interop.LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
		EntryPoint = "duckdb_data_chunk_get_size")]
	[SuppressGCTransition]
	private static extern long GetRowsCount(nint chunk);
}
