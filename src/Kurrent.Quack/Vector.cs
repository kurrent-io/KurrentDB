// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DuckDB.NET.Native;

namespace Kurrent.Quack;

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

	private static bool IsNotNull(ulong* validity, long rowIndex) {
		if (validity is null)
			return true;

		var validityMaskEntryIndex = rowIndex / Int64BitSize;
		var validityBitIndex = (int)(rowIndex % Int64BitSize);
		var validityBit = 1UL << validityBitIndex;

		return (validity[validityMaskEntryIndex] & validityBit) is not 0UL;
	}

	internal bool IsNullable => _validity is not null;

	internal bool IsNotNull(long rowIndex) => IsNotNull(_validity, rowIndex);

	internal T? TryRead<T>(long rowIndex)
		where T : unmanaged {
		return IsNotNull(rowIndex)
			? *Read<T>(rowIndex)
			: null;
	}

	internal T* Read<T>(long rowIndex) where T : unmanaged => &((T*)_columnData)[rowIndex];

	internal Blob ReadBlob(long rowIndex) => new(Read<DuckDBString>(rowIndex));

	internal Blob? TryReadBlob(long rowIndex)
		=> IsNotNull(rowIndex)
			? ReadBlob(rowIndex)
			: null;

	internal ReadOnlySpan<T> GetRows<T>(int length) where T : unmanaged => new(_columnData, length);
}
