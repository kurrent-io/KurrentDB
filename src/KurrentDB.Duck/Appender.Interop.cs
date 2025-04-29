// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using DuckDB.NET.Native;

namespace KurrentDB.Duck;

using Marshallers;

partial struct Appender {
	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_appender_create")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Create(nint connection, nint schema, in byte tableNameUtf8NullTerminated,
		out nint appender);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_appender_error")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial nint GetErrorString(nint appender);

	static nint INativeWrapper<Appender>.GetErrorString(nint appender) => GetErrorString(appender);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_appender_destroy")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Destroy(in nint appender);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_appender_flush")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Flush(nint appender);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_appender_end_row")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState EndRow(nint appender);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_append_int32")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Append(nint appender, int value);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_append_uint32")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Append(nint appender, uint value);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_append_int64")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Append(nint appender, long value);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_append_uint64")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Append(nint appender, ulong value);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_append_null")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState AppendNull(nint appender);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_append_default")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState AppendDefault(nint appender);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_append_timestamp")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState AppendTimestamp(nint appender, long microseconds);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_append_varchar_length")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState AppendVarChar(nint appender, in byte utf8String, long length);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_append_blob")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState AppendBlob(nint appender, in byte bytes, long length);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_append_float")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Append(nint appender, float value);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_append_double")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Append(nint appender, double value);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_append_hugeint")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Append(nint appender, [MarshalUsing(typeof(HugeIntMarshaller))] Int128 value);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_append_uhugeint")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Append(nint appender, [MarshalUsing(typeof(HugeIntMarshaller))] UInt128 value);

	[LibraryImport(Interop.LibraryName, EntryPoint = "duckdb_append_bool")]
	[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	private static partial DuckDBState Append(nint appender, [MarshalAs(UnmanagedType.Bool)] bool value);
}
