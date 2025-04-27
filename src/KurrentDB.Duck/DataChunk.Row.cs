// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNext.Buffers.Binary;
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

		private unsafe T Read<T>()
			where T : unmanaged
			=> *_chunk.Columns[_columnIndex++].Read<T>(_chunk._rowIndex);

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

		public Blob ReadBlob()
			=> _chunk.Columns[_columnIndex++].ReadBlob(_chunk._rowIndex);

		public Blob? TryReadBlob()
			=> _chunk.Columns[_columnIndex++].TryReadBlob(_chunk._rowIndex);

		public string ReadString() => ReadBlob().Reference.ToUtf16String();

		public string? TryReadString() => TryReadBlob()?.Reference.ToUtf16String();

		public DateTime ReadDateTime()
			=> NativeMethods.DateTimeHelpers.DuckDBFromTimestamp(Read<DuckDBTimestampStruct>()).ToDateTime();

		public DateTime? TryReadDateTime()
			=> TryRead<DuckDBTimestampStruct>() is { } ts
				? NativeMethods.DateTimeHelpers.DuckDBFromTimestamp(ts).ToDateTime()
				: null;

		public T ReadBlob<T>()
			where T : struct, IBinaryFormattable<T> {
			return T.Parse(ReadBlob().Reference.AsSpan());
		}

		public T? TryReadBlob<T>()
			where T : struct, IBinaryFormattable<T>
			=> TryReadBlob() is { } blob ? T.Parse(blob.Reference.AsSpan()) : null;
	}
}
