// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.Marshalling;
using DuckDB.NET.Native;

namespace KurrentDB.Duck.Marshallers;

[CustomMarshaller(typeof(Int128), MarshalMode.ManagedToUnmanagedIn, typeof(HugeIntMarshaller))]
[CustomMarshaller(typeof(UInt128), MarshalMode.ManagedToUnmanagedIn, typeof(HugeIntMarshaller))]
internal static class HugeIntMarshaller {
	public static DuckDBHugeInt ConvertToUnmanaged(Int128 value)
		=> BitConverter.IsLittleEndian ? Unsafe.BitCast<Int128, DuckDBHugeInt>(value) : new(value);

	public static DuckDBUHugeInt ConvertToUnmanaged(UInt128 value)
		=> BitConverter.IsLittleEndian ? Unsafe.BitCast<UInt128, DuckDBUHugeInt>(value) : new(value);
}
