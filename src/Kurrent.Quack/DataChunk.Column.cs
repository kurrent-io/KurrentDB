// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Runtime.InteropServices;
using DuckDB.NET.Native;

namespace Kurrent.Quack;

partial struct DataChunk {

	/// <summary>
	/// Represents column data.
	/// </summary>
	[StructLayout(LayoutKind.Auto)]
	public readonly struct Column {
		private readonly Vector _vector;
		private readonly int _rowCount;

		internal Column(Vector vector, int rowCount) {
			_vector = vector;
			_rowCount = rowCount;
		}

		public bool this[int rowIndex] {
			get {
				ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)rowIndex, (uint)_rowCount,
					nameof(rowIndex));

				return _vector.IsNull(rowIndex);
			}
		}

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<int> Int32Data => _vector.GetRows<int>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<uint> UInt32Data => _vector.GetRows<uint>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<long> Int64Data => _vector.GetRows<long>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<ulong> UInt64Data => _vector.GetRows<ulong>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<DuckDBString> BlobData => _vector.GetRows<DuckDBString>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<float> FloatData => _vector.GetRows<float>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<double> DoubleData => _vector.GetRows<double>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<Int128> Int128Data => _vector.GetRows<Int128>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<UInt128> UInt128Data => _vector.GetRows<UInt128>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<bool> BooleanData => _vector.GetRows<bool>(_rowCount);
	}

	public Column this[int index] => new(Columns[index], int.CreateChecked(RowsCount));
}
