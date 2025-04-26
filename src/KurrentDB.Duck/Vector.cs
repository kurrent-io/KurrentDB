// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DuckDB.NET.Native;

namespace KurrentDB.Duck;

[StructLayout(LayoutKind.Auto)]
internal readonly unsafe partial struct Vector {
	private const int Int64BitSize = sizeof(long) * 8;

	private readonly void* _columnData;
	private readonly ulong* _validity;

	internal Vector(nint dataChunk, long columnIndex) {
		var vector = GetVector(dataChunk, columnIndex);
		_columnData = GetVectorData(vector);
		_validity = GetValidity(vector);
	}

	private static bool IsNull(ulong* validity, long rowIndex) {
		if (validity is null)
			return false;

		var validityMaskEntryIndex = rowIndex / Int64BitSize;
		var validityBitIndex = (int)(rowIndex % Int64BitSize);
		var validityBit = 1UL << validityBitIndex;

		return (validity[validityMaskEntryIndex] & validityBit) is 0UL;
	}

	internal bool IsNull(long rowIndex) => IsNull(_validity, rowIndex);

	internal T? TryRead<T>(long rowIndex)
		where T : unmanaged {
		return IsNull(rowIndex)
			? null
			: *Read<T>(rowIndex);
	}

	internal T* Read<T>(long rowIndex) where T : unmanaged => &((T*)_columnData)[rowIndex];

	internal Blob ReadBlob(long rowIndex) => new(Read<DuckDBString>(rowIndex));

	internal Blob? TryReadBlob(long rowIndex) {
		if (IsNull(rowIndex)) {
			return null;
		}

		return ReadBlob(rowIndex);
	}

	internal ReadOnlySpan<T> GetRows<T>(int length) where T : unmanaged => new(_columnData, length);
}
