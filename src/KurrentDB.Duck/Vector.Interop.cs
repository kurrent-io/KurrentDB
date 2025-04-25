// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KurrentDB.Duck;

partial struct Vector {

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_data_chunk_get_vector")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial nint GetVector(nint dataChunk, long columnIndex);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_vector_get_data")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial nint GetVectorData(nint vector);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_vector_get_validity")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial nint GetValidity(nint vector);
}
