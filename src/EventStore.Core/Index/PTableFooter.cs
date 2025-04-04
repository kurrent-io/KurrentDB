// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using EventStore.Core.Exceptions;

namespace EventStore.Core.Index;

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

	public byte[] AsByteArray() {
		var array = new byte[Size];
		array[0] = (byte)FileType.PTableFile;
		array[1] = Version;
		uint numMidpoints = NumMidpointsCached;
		for (int i = 0; i < 4; i++) {
			array[i + 2] = (byte)(numMidpoints & 0xFF);
			numMidpoints >>= 8;
		}

		return array;
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

		byte[] buffer = new byte[4];
		stream.Read(buffer, 0, 4);
		uint numMidpointsCached = BitConverter.ToUInt32(buffer, 0);

		return new PTableFooter((byte)version, numMidpointsCached);
	}
}
