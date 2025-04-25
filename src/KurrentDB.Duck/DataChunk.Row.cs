// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DuckDB.NET.Native;

namespace KurrentDB.Duck;

partial struct DataChunk {

	/// <summary>
	/// Represents a row within the data chunk.
	/// </summary>
	[StructLayout(LayoutKind.Auto)]
	public ref struct Row {
		private readonly ref readonly DataChunk _chunk;
		private int _columnIndex;

		internal Row(ref readonly DataChunk chunk) => _chunk = ref chunk;

		private T Read<T>()
			where T : unmanaged
			=> _chunk.Columns[_columnIndex++].Read<T>(_chunk._rowIndex);

		private T? TryRead<T>()
			where T : unmanaged
			=> _chunk.Columns[_columnIndex++].TryRead<T>(_chunk._rowIndex);

		public int? TryReadInt32()
			=> TryRead<int>();

		public int ReadInt32()
			=> Read<int>();

		public long? TryReadInt64()
			=> TryRead<long>();

		public long ReadInt64()
			=> Read<long>();

		public ulong? TryReadUInt64()
			=> TryRead<ulong>();

		public ulong ReadUInt64()
			=> Read<ulong>();

		public uint? TryReadUInt32()
			=> TryRead<uint>();

		public uint ReadUInt32()
			=> Read<uint>();

		public ReadOnlySpan<byte> ReadBlob()
			=> _chunk.Columns[_columnIndex++].ReadBitString(_chunk._rowIndex);

		public bool TryReadBlob(out ReadOnlySpan<byte> buffer)
			=> _chunk.Columns[_columnIndex++].ReadBitString(_chunk._rowIndex, out buffer);

		[SkipLocalsInit]
		public string ReadString() => Interop.ToUtf16String(ReadBlob());

		[SkipLocalsInit]
		public string? TryReadString()
			=> TryReadBlob(out var buffer) ? Interop.ToUtf16String(buffer) : null;

		public DateTime ReadDateTime()
			=> NativeMethods.DateTimeHelpers.DuckDBFromTimestamp(Read<DuckDBTimestampStruct>()).ToDateTime();

		public DateTime? TryReadDateTime()
			=> TryRead<DuckDBTimestampStruct>() is { } ts
				? NativeMethods.DateTimeHelpers.DuckDBFromTimestamp(ts).ToDateTime()
				: null;
	}
}
