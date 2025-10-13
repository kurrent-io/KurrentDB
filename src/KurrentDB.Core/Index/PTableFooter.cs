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
public readonly struct PTableFooter : IBinaryFormattable<PTableFooter> {
	public const int Size = 128;
	public readonly FileType FileType;
	public readonly byte Version;
	public readonly uint NumMidpointsCached;

	public PTableFooter(byte version, uint numMidpointsCached) {
		FileType = FileType.PTableFile;
		Version = version;
		NumMidpointsCached = numMidpointsCached;
	}

	private PTableFooter(ReadOnlySpan<byte> buffer) {
		var reader = new SpanReader<byte>(buffer);
		FileType = reader.Read() is (byte)FileType.PTableFile
			? FileType.PTableFile
			: throw new CorruptIndexException("Corrupted PTable.", new InvalidFileException("Wrong type of PTable."));

		Version = reader.Read();
		if (Version < PTableVersions.IndexV4)
			throw new CorruptIndexException(
				"PTable footer with version < 4 found. PTable footers are supported as from version 4.",
				new InvalidFileException("Invalid PTable file."));

		NumMidpointsCached = reader.ReadLittleEndian<uint>();
	}

	public static int GetSize(byte version)
		=> version >= PTableVersions.IndexV4 ? Size : 0;

	static int IBinaryFormattable<PTableFooter>.Size => Size;

	public static PTableFooter Parse(ReadOnlySpan<byte> source) => new(source);

	public static PTableFooter Parse(SafeFileHandle handle, long fileOffset) {
		Span<byte> buffer = stackalloc byte[Size];
		return RandomAccess.Read(handle, buffer, fileOffset) == buffer.Length
			? Parse(buffer)
			: throw new CorruptIndexException("Corrupted PTable footer.", new InvalidFileException("Wrong file size."));
	}

	public void Format(Span<byte> buffer) {
		var writer = new SpanWriter<byte>(buffer);
		writer.Add((byte)FileType);
		writer.Add(Version);
		writer.WriteLittleEndian(NumMidpointsCached);
	}

	public byte[] AsByteArray() {
		var result = new byte[Size];
		Format(result);
		return result;
	}
}
