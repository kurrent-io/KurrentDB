// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using DuckDB.NET.Native;

namespace Kurrent.Quack;

using Marshallers;

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

	[DllImport(Interop.LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "duckdb_nparams")]
	[SuppressGCTransition]
	private static extern long GetParametersCount(nint preparedStatement);

	[DllImport(Interop.LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "duckdb_bind_null")]
	[SuppressGCTransition]
	private static extern DuckDBState BindNull(nint preparedStatement, long index);

	[DllImport(Interop.LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "duckdb_bind_int32")]
	[SuppressGCTransition]
	private static extern DuckDBState Bind(nint preparedStatement, long index, int value);

	[DllImport(Interop.LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "duckdb_bind_uint32")]
	[SuppressGCTransition]
	private static extern DuckDBState Bind(nint preparedStatement, long index, uint value);

	[DllImport(Interop.LibraryName, EntryPoint = "duckdb_bind_int64")]
	[SuppressGCTransition]
	private static extern DuckDBState Bind(nint preparedStatement, long index, long value);

	[DllImport(Interop.LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "duckdb_bind_uint64")]
	[SuppressGCTransition]
	private static extern DuckDBState Bind(nint preparedStatement, long index, ulong value);

	[DllImport(Interop.LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "duckdb_bind_timestamp")]
	[SuppressGCTransition]
	private static extern DuckDBState BindTimestamp(nint preparedStatement, long index, long microseconds);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_bind_blob")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState BindBlob(nint preparedStatement, long index, in byte data, long length);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_bind_varchar_length")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState BindVarChar(nint preparedStatement, long index, in byte utf8String, long length);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_bind_boolean")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Bind(nint preparedStatement, long index,
		[MarshalUsing(typeof(CppBooleanMarshaller))] bool value);

	[DllImport(Interop.LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "duckdb_bind_float")]
	[SuppressGCTransition]
	private static extern DuckDBState Bind(nint preparedStatement, long index, float value);

	[DllImport(Interop.LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "duckdb_bind_double")]
	[SuppressGCTransition]
	private static extern DuckDBState Bind(nint preparedStatement, long index, double value);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_bind_hugeint")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Bind(nint preparedStatement, long index,
		[MarshalUsing(typeof(HugeIntMarshaller))] Int128 value);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_bind_uhugeint")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Bind(nint preparedStatement, long index,
		[MarshalUsing(typeof(HugeIntMarshaller))] UInt128 value);
}
