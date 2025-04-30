// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNext;

namespace Kurrent.Quack;

/// <summary>
/// Represents table chunk.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public partial struct DataChunk : IResettable {
	private const int MaxColumnCount = 32; // 32 columns X 16 bytes per vector = 512 bytes on the stack

	private readonly ColumnArray _columns;
	private long _rowIndex;

	internal DataChunk(nint dataChunk) {
		RowsCount = GetRowsCount(dataChunk);
		ColumnsCount = checked((int)GetColumnCount(dataChunk));

		Span<Vector> columns = _columns;
		for (var i = 0; i < Math.Min(ColumnsCount, MaxColumnCount); i++) {
			columns[i] = new(dataChunk, i);
		}

		Reset();
	}

	[UnscopedRef]
	[DebuggerBrowsable(DebuggerBrowsableState.Never)]
	private ReadOnlySpan<Vector> Columns {
		get {
			ReadOnlySpan<Vector> columns = _columns;
			return columns.Slice(0, ColumnsCount);
		}
	}

	[UnscopedRef]
	public bool TryRead(out Row row) {
		if (_rowIndex >= RowsCount - 1L) {
			row = default;
			return false;
		}

		_rowIndex++;
		row = new(in this);
		return true;
	}

	public bool TryRead<TRow, TParser>(out TRow row)
		where TRow : struct
		where TParser : IDataRowParser<TRow> {
		if (TryRead(out Row internalRow)) {
			row = TParser.Parse(ref internalRow);
			return true;
		}

		row = default;
		return false;
	}

	/// <summary>
	/// Resets the internal cursor.
	/// </summary>
	public void Reset() => _rowIndex = -1L;

	/// <summary>
	/// Gets the number of rows in this chunk.
	/// </summary>
	public readonly long RowsCount { get; }

	/// <summary>
	/// Gets the number of columns in this chunk.
	/// </summary>
	public readonly int ColumnsCount { get; }

	[InlineArray(MaxColumnCount)]
	private struct ColumnArray {
		private Vector _vector;
	}
}
