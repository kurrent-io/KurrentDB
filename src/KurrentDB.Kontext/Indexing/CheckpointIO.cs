// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Buffers.Binary;

namespace KurrentDB.Kontext.Indexing;

internal sealed class CheckpointIO : IDisposable {
	const int PayloadSize = 16;

	readonly FileStream _stream;

	public CheckpointIO(string path) {
		_stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
	}

	public (ulong Commit, ulong Prepare)? Load() {
		if (_stream.Length != PayloadSize)
			return null;

		Span<byte> buffer = stackalloc byte[PayloadSize];
		_stream.Position = 0;
		_stream.ReadExactly(buffer);

		var commit = BinaryPrimitives.ReadUInt64LittleEndian(buffer);
		var prepare = BinaryPrimitives.ReadUInt64LittleEndian(buffer[8..]);
		return (commit, prepare);
	}

	public void Save(ulong commit, ulong prepare) {
		Span<byte> buffer = stackalloc byte[PayloadSize];
		BinaryPrimitives.WriteUInt64LittleEndian(buffer, commit);
		BinaryPrimitives.WriteUInt64LittleEndian(buffer[8..], prepare);

		_stream.Position = 0;
		_stream.Write(buffer);
		_stream.Flush(flushToDisk: true);
	}

	public void Dispose() => _stream.Dispose();
}