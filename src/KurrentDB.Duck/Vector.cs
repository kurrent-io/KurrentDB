// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DuckDB.NET.Native;

namespace KurrentDB.Duck;

[StructLayout(LayoutKind.Auto)]
internal readonly partial struct Vector {
	private const int Int64BitSize = sizeof(long) * 8;

	private readonly nint _columnData;
	private readonly nint _validity;

	internal Vector(nint dataChunk, long columnIndex) {
		var vector = GetVector(dataChunk, columnIndex);
		_columnData = GetVectorData(vector);
		_validity = GetValidity(vector);
	}

	private unsafe bool IsNull(long rowIndex) {
		if (_validity is 0)
			return false;

		var validityMaskEntryIndex = rowIndex / Int64BitSize;
		var validityBitIndex = (int)(rowIndex % Int64BitSize);
		var validityBit = 1UL << validityBitIndex;

		ref ulong validityFlag = ref ((ulong*)_validity)[validityMaskEntryIndex];
		return (validityFlag & validityBit) is 0UL;
	}

	internal T? TryRead<T>(long rowIndex)
		where T : unmanaged {
		return IsNull(rowIndex)
			? null
			: Read<T>(rowIndex);
	}

	internal unsafe ref readonly T Read<T>(long rowIndex) where T : unmanaged => ref ((T*)_columnData)[rowIndex];

	internal unsafe ReadOnlySpan<byte> ReadBitString(long rowIndex) {
		ref readonly var str = ref Read<DuckDBString>(rowIndex);
		return new(str.Data, str.Length);
	}

	internal unsafe bool ReadBitString(long rowIndex, out ReadOnlySpan<byte> buffer) {
		if (IsNull(rowIndex)) {
			buffer = default;
			return false;
		}

		ref readonly var str = ref Read<DuckDBString>(rowIndex);
		buffer = new(str.Data, str.Length);
		return true;
	}
}
