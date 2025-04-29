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

		/// <summary>
		/// Indicates that the column contains rows with null values.
		/// </summary>
		public bool IsNullable => _vector.IsNullable;

		/// <summary>
		/// Determines whether the specified row in this column is null.
		/// </summary>
		/// <param name="rowIndex">Zero-based row index.</param>
		public bool this[int rowIndex] {
			get {
				ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)rowIndex, (uint)_rowCount,
					nameof(rowIndex));

				return _vector.IsNull(rowIndex);
			}
		}

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<int> Int32Rows => _vector.GetRows<int>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<uint> UInt32Rows => _vector.GetRows<uint>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<long> Int64Rows => _vector.GetRows<long>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<ulong> UInt64Rows => _vector.GetRows<ulong>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<DuckDBString> BlobRows => _vector.GetRows<DuckDBString>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<float> FloatRows => _vector.GetRows<float>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<double> DoubleRows => _vector.GetRows<double>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<Int128> Int128Rows => _vector.GetRows<Int128>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<UInt128> UInt128Rows => _vector.GetRows<UInt128>(_rowCount);

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public ReadOnlySpan<bool> BooleanRows => _vector.GetRows<bool>(_rowCount);
	}

	public Column this[int index] => new(Columns[index], int.CreateChecked(RowsCount));
}
