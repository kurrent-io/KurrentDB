// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using EventStore.Common.Utils;
using Serilog;

namespace EventStore.Core.DataStructures.ProbabilisticFilter;

public unsafe class MemoryMappedFilePersistence : IPersistenceStrategy {
	public static readonly ILogger Log = Serilog.Log.ForContext<MemoryMappedFilePersistence>();
	private readonly long _size;
	private readonly string _path;
	private MemoryMappedFile _mmf;
	private MemoryMappedViewAccessor _mmfWriteAccessor;
	private FileStream _fileStream;
	private bool _disposed;

	public MemoryMappedFilePersistence(long size, string path, bool create) {
		Ensure.NotNull(path, nameof(path));

		_size = size;
		_path = path;
		Create = create;
	}

	public BloomFilterAccessor DataAccessor { get; private set; }
	public bool Create { get; }

	public void Init() {
		DataAccessor = new BloomFilterAccessor(
			logicalFilterSize: _size,
			cacheLineSize: BloomFilterIntegrity.CacheLineSize,
			hashSize: BloomFilterIntegrity.HashSize,
			pageSize: BloomFilterIntegrity.PageSize,
			onPageDirty: null,
			log: Log);

		_fileStream = new FileStream(
			_path,
			// todo: OpenOrCreate can be CreateNew if the mininode closed the file before declaring itself 'stopped'
			Create ? FileMode.OpenOrCreate : FileMode.Open,
			FileAccess.ReadWrite,
			FileShare.ReadWrite);

		if (Create) {
			_fileStream.SetLength(DataAccessor.FileSize);
		} else {
			// existing
			if (_fileStream.Length != DataAccessor.FileSize)
				throw new SizeMismatchException(
					$"The expected file size ({DataAccessor.FileSize:N0}) does not match " +
					$"the actual file size ({_fileStream.Length:N0}) of file {_path}");
		}

		_mmf = MemoryMappedFile.CreateFromFile(
			fileStream: _fileStream,
			mapName: null,
			capacity: 0,
			access: MemoryMappedFileAccess.ReadWrite,
			inheritability: HandleInheritability.None,
			leaveOpen: false);

		_mmfWriteAccessor = _mmf.CreateViewAccessor(
			offset: DataAccessor.CacheLineSize,
			size: 0, // whole file
			access: MemoryMappedFileAccess.ReadWrite);

		// acquired pointer unaffected by the accessor offset
		// to the beginning of the file (i.e. the header)
		byte* pointer = null;
		_mmfWriteAccessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
		DataAccessor.Pointer = pointer;

		if (Create) {
			DataAccessor.FillWithZeros();
		} else {
			// do not fill with zeros; would overwrite the data.
		}
	}

	public void Flush() {
		_mmfWriteAccessor.Flush();
		_fileStream.FlushToDisk();
	}

	// todo later: maybe could be a common implementation across the strategies that reads
	// from the DataAccessor
	public Header ReadHeader() {
		try {
			//read the version first
			using (var headerAccessor = _mmf.CreateViewAccessor(0, 1, MemoryMappedFileAccess.Read)) {
				byte version = headerAccessor.ReadByte(0);
				if (version != Header.CurrentVersion) {
					throw new CorruptedFileException($"Unsupported version: {version}");
				}
			}

			//then the full header
			var headerBytes = new byte[Header.Size].AsSpan();
			using (var headerAccessor = _mmf.CreateViewStream(0, Header.Size, MemoryMappedFileAccess.Read)) {
				int read = headerAccessor.Read(headerBytes);
				if (read != Header.Size) {
					throw new CorruptedFileException(
						$"File header size ({read} bytes) does not match expected header size ({Header.Size} bytes)");
				}
			}

			return MemoryMarshal.AsRef<Header>(headerBytes);
		} catch (Exception exc) when (exc is not CorruptedFileException) {
			throw new CorruptedFileException("Failed to read the header", exc);
		}
	}

	public void WriteHeader(Header header) {
		var span = MemoryMarshal.CreateReadOnlySpan(ref header, 1);
		var headerBytes = MemoryMarshal.Cast<Header, byte>(span);
		using var headerAccessor = _mmf.CreateViewStream(0, Header.Size, MemoryMappedFileAccess.Write);
		headerAccessor.Write(headerBytes);
		headerAccessor.Flush();
	}

	public void Dispose() {
		if (_disposed)
			return;

		_disposed = true;

		if (DataAccessor is not null)
			DataAccessor.Pointer = default;
		_mmfWriteAccessor?.SafeMemoryMappedViewHandle.ReleasePointer();
		_mmfWriteAccessor?.Dispose();
		_mmf?.Dispose();
	}
}
