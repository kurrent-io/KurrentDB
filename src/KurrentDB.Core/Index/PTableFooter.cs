// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using KurrentDB.Core.Exceptions;

namespace KurrentDB.Core.Index;

public class PTableFooter {
	private const int Size = 128;
	public readonly FileType FileType;
	public readonly byte Version;
	public readonly uint NumMidpointsCached;

	public static int GetSize(byte version) {
		if (version >= PTableVersions.IndexV4)
			return Size;
		return 0;
	}

	public PTableFooter(byte version, uint numMidpointsCached) {
		FileType = FileType.PTableFile;
		Version = version;
		NumMidpointsCached = numMidpointsCached;
	}

	[SkipLocalsInit]
	public void WriteTo(Stream stream) {
		Span<byte> buffer = stackalloc byte[Size];
		buffer.Clear();

		buffer[0] = (byte)FileType.PTableFile;
		buffer[1] = Version;
		BinaryPrimitives.WriteUInt32LittleEndian(buffer[2..], NumMidpointsCached);

		stream.Write(buffer);
	}

	public static PTableFooter FromStream(Stream stream) {
		var type = stream.ReadByte();
		if (type != (int)FileType.PTableFile)
			throw new CorruptIndexException("Corrupted PTable.", new InvalidFileException("Wrong type of PTable."));
		var version = stream.ReadByte();
		if (version == -1)
			throw new CorruptIndexException("Couldn't read version of PTable from footer.",
				new InvalidFileException("Invalid PTable file."));
		if (!(version >= PTableVersions.IndexV4))
			throw new CorruptIndexException(
				"PTable footer with version < 4 found. PTable footers are supported as from version 4.",
				new InvalidFileException("Invalid PTable file."));

		Span<byte> buffer = stackalloc byte[4];
		stream.ReadExactly(buffer);
		uint numMidpointsCached = BinaryPrimitives.ReadUInt32LittleEndian(buffer);

		return new PTableFooter((byte)version, numMidpointsCached);
	}
}
