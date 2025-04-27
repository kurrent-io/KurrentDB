// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;

namespace KurrentDB.Duck;

partial struct Vector {

	[DllImport(Interop.LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
		EntryPoint = "duckdb_data_chunk_get_vector")]
	[SuppressGCTransition]
	private static extern nint GetVector(nint dataChunk, long columnIndex);

	[DllImport(Interop.LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
		EntryPoint = "duckdb_vector_get_data")]
	[SuppressGCTransition]
	private static extern unsafe void* GetVectorData(nint vector);

	[DllImport(Interop.LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true,
		EntryPoint = "duckdb_vector_get_validity")]
	[SuppressGCTransition]
	private static extern unsafe ulong* GetValidity(nint vector);
}
