// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Runtime.InteropServices;
using DotNext.Buffers;
using DotNext.Buffers.Binary;
using KurrentDB.Core.Exceptions;
using Microsoft.Win32.SafeHandles;

namespace KurrentDB.Core.Index;

[StructLayout(LayoutKind.Auto)]
public readonly struct PTableHeader : IBinaryFormattable<PTableHeader> {
	public const int Size = 128;
	private const byte FileType = (byte)Index.FileType.PTableFile;

	public readonly byte Version;

	public PTableHeader(byte version) {
		Version = version;
	}

	private PTableHeader(ref SpanReader<byte> reader) {
		if (reader.Read() is not FileType)
			throw new CorruptIndexException("Corrupted PTable.", new InvalidFileException("Wrong type of PTable."));

		Version = reader.Read();
	}

	public void Format(Span<byte> destination) {
		var writer = new SpanWriter<byte>(destination);
		writer.Add(FileType);
		writer.Add(Version);
	}

	public byte[] AsByteArray() {
		var result = new byte[Size];
		Format(result);
		return result;
	}

	public static PTableHeader Parse(ReadOnlySpan<byte> source) {
		var reader = new SpanReader<byte>(source);
		return new(ref reader);
	}

	public static PTableHeader Parse(SafeFileHandle handle, long fileOffset) {
		Span<byte> buffer = stackalloc byte[Size];
		return RandomAccess.Read(handle, buffer, fileOffset) == buffer.Length
			? Parse(buffer)
			: throw new CorruptIndexException("Corrupted PTable header.", new InvalidFileException("Wrong file size."));
	}

	static int IBinaryFormattable<PTableHeader>.Size => Size;
}
