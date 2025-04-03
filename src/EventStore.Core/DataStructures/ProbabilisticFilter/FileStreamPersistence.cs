// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using EventStore.Common.Utils;
using Serilog;

namespace EventStore.Core.DataStructures.ProbabilisticFilter;

public unsafe class FileStreamPersistence : IPersistenceStrategy {
	protected static readonly ILogger Log = Serilog.Log.ForContext<FileStreamPersistence>();

	// We synchronize access to _dirtyPageBitmap because
	// Flush can be called at the same time as OnPageDirty (we dont pause writes into the
	// bloom filter while it is flushing). And they both write to the bitmap (to unset and
	// set bits respectively)
	// This lock is often obtained while holding the write lock to the filter
	// therefore don't make any non-trivial external calls while holding this lock
	private readonly object _bitmapLock = new();
	private readonly long _logicalFilterSize;
	private readonly string _path;
	private AlignedMemory _bloomFilterMemory;
	private AlignedMemory _dirtyPageBitmap;
	private bool _disposed;

	public FileStreamPersistence(long size, string path, bool create) {
		Ensure.NotNull(path, nameof(path));
		_logicalFilterSize = size;
		_path = path;
		Create = create;
	}

	// when we want to open an existing filter and we don't mind what size it is
	public static FileStreamPersistence FromFile(string path) {
		Ensure.NotNull(path, nameof(path));

		var header = ReadHeader(path);
		return new FileStreamPersistence(
			size: header.NumBits / 8,
			path: path,
			create: false);
	}

	public BloomFilterAccessor DataAccessor { get; private set; }
	public bool Create { get; }

	// ms to wait after each batch flush. allows breathing room for other writes to continue.
	public int FlushBatchDelay { get; private set; }

	// max number of pages to flush to disk in each batch
	public long FlushBatchSize { get; private set; }

	public void Init() {
		DataAccessor = new BloomFilterAccessor(
			logicalFilterSize: _logicalFilterSize,
			cacheLineSize: BloomFilterIntegrity.CacheLineSize,
			hashSize: BloomFilterIntegrity.HashSize,
			pageSize: BloomFilterIntegrity.PageSize,
			onPageDirty: OnPageDirty,
			log: Log);

		// instead of flushing all the dirty pages as fast as we can, we flush them in batches
		// with a sleep between each flush to allow other writes to proceed.
		// we could consider making these configurable but we have set defaults as follows
		// based on 8 KiB pages:
		//  1. sleep 128ms between batches to gives a good chunk of time for other writes
		//  2. calculate how many pages to flush per batch in order to give no more than 60s
		//     of sleep total. if the whole filter is dirty, total sleep will add up to 60s.
		// this will yield bigger batches for bigger filters, but they ought also to be running
		// on faster disks.
		//
		// For default filter this yields a batch size of 96 pages, 0.75 MiB
		// For max size filter this yield a batch size of 1120 pages, 8.75 MiB
		// For PTables up to 24gb this yields a batch size of 96 pages.
		FlushBatchDelay = 128;
		FlushBatchSize = DataAccessor.NumPages * FlushBatchDelay / 60_000;
		FlushBatchSize = Math.Max(FlushBatchSize, 96);
		FlushBatchSize = FlushBatchSize.RoundUpToMultipleOf(32);

		// dirtypages: one bit per page, but pad to the nearest cacheline boundary
		var numBits = DataAccessor.NumPages;
		var numBitsPadded = numBits.RoundUpToMultipleOf(BloomFilterIntegrity.CacheLineSize * 8);
		_dirtyPageBitmap = new AlignedMemory(
			size: numBitsPadded / 8,
			alignTo: BloomFilterIntegrity.CacheLineSize);
		_dirtyPageBitmap.AsSpan().Clear(); // alignedmemory isn't initialized otherwise

		// main filter:
		_bloomFilterMemory = new AlignedMemory(
			size: new IntPtr(DataAccessor.FileSize),
			alignTo: BloomFilterIntegrity.CacheLineSize);
		DataAccessor.Pointer = _bloomFilterMemory.Pointer;

		// initialize the aligned memory
		if (Create) {
			DataAccessor.FillWithZeros();
		} else {
			// load the whole filter into memory for rapid access
			BulkLoadExisting();
		}
	}

	private void BulkLoadExisting() {
		Log.Information(
			"Reading persisted bloom filter {path} of size {size:N0} bytes into memory...",
			_path,
			DataAccessor.FileSize);

		var sw = Stopwatch.StartNew();

		using var bulkFileStream = new FileStream(
			_path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite,
			bufferSize: 65_536,
			// consider if we should do something similar to chunk file reduce file cache pressure?
			options: FileOptions.SequentialScan);

		if (bulkFileStream.Length != DataAccessor.FileSize)
			throw new SizeMismatchException(
				$"The expected file size ({DataAccessor.FileSize:N0}) does not match " +
				$"the actual file size ({bulkFileStream.Length:N0}) of file {_path}");

		// linux only reads 2147479552 at a time (4095 bytes less than intmax)
		var blockSize = int.MaxValue / 2;
		var bytesToRead = DataAccessor.FileSize;
		var bytesRead = 0L;
		while (bytesToRead > 0) {
			var bytesToReadInBlock = bytesToRead > blockSize
				? blockSize
				: (int)bytesToRead;
			
			// consider reading in buffer size blocks. or using random access in net6
			var read = bulkFileStream.Read(new Span<byte>(DataAccessor.Pointer + bytesRead, bytesToReadInBlock));
			if (read != bytesToReadInBlock)
				throw new Exception($"Read fewer bytes ({read}) from bloom filter ({_path}) than expected ({bytesToReadInBlock})");
			bytesRead += bytesToReadInBlock;
			bytesToRead -= bytesToReadInBlock;
		}

		var elapsed = sw.Elapsed;
		var fileSizeMb = DataAccessor.FileSize / 1000 / 1000;
		var megaBytesPerSecond = fileSizeMb / elapsed.TotalSeconds;
		Log.Information(
			"Read persisted bloom filter {path} into memory. Took {elapsed}. {megaBytesPerSecond:N2} MB/s",
			_path,
			elapsed,
			megaBytesPerSecond);
	}

	private void OnPageDirty(long pageNumber) {
		lock (_bitmapLock) {
			ThrowIfDisposed();
			var byteIndex = (int)(pageNumber / 8);
			var bitIndex = pageNumber % 8;
			ref var byteValue = ref _dirtyPageBitmap.AsSpan()[byteIndex];
			byteValue = byteValue.SetBit(bitIndex);
		}
	}

	public void Flush() {
		using var fileStream = new FileStream(
			_path,
			FileMode.OpenOrCreate,
			FileAccess.ReadWrite,
			FileShare.ReadWrite,
			bufferSize: DataAccessor.PageSize);

		fileStream.SetLength(DataAccessor.FileSize);
		Span<byte> localCacheLine = stackalloc byte[BloomFilterIntegrity.CacheLineSize];
		localCacheLine.Clear();

		var pageNumber = 0L;
		var flushedPages = 0L;
		var pauses = 0;

		var activelyFlushing = Stopwatch.StartNew();
		// consider Interlocked.Or/And into/outof the dirty page map. could remove need for lock
		for (
			var remaining = _dirtyPageBitmap.AsSpan();
			!remaining.IsEmpty;
			remaining = remaining[BloomFilterIntegrity.CacheLineSize..]
			) {

			lock (_bitmapLock) {
				ThrowIfDisposed();
				var cacheLine = remaining[..BloomFilterIntegrity.CacheLineSize];
				cacheLine.CopyTo(localCacheLine);
				cacheLine.Clear();
			}

			foreach (var @byte in localCacheLine) {
				//we could skip based on 0L without checking each bit/byte
				for (var bitOffset = 0; bitOffset < 8; bitOffset++) {
					if (@byte.IsBitSet(bitOffset)) {
						WritePage(pageNumber, fileStream);
						flushedPages++;

						// could be an unnecessary delay at the end
						if (flushedPages % FlushBatchSize == 0) {
							fileStream.FlushToDisk();
							activelyFlushing.Stop();
							pauses++;
							Thread.Sleep(FlushBatchDelay);
							activelyFlushing.Start();
						}
					}

					pageNumber++;
					if (pageNumber == DataAccessor.NumPages)
						goto Done;
				}
			}
		}

		Done:
		fileStream.FlushToDisk();

		activelyFlushing.Stop();

		var flushedBytes = flushedPages * DataAccessor.PageSize;
		var flushedMegaBytes = (float)flushedBytes / 1000 / 1000;
		var activeFlushRateMBperS = flushedMegaBytes / activelyFlushing.Elapsed.TotalSeconds;

		Log.Verbose(
			"Flushed {pages:N0} pages out of {totalPages:N0}. {bytes:N0} bytes. " +
			"Delay {delay} ms per batch. Total delay {totalDelay:N0} ms. " +
			"Actively flushing: {activeFlushTime} {activeFlushRate:N2} MB/s. ",
			flushedPages, DataAccessor.NumPages, flushedBytes,
			FlushBatchDelay, FlushBatchDelay * pauses,
			activelyFlushing.Elapsed, activeFlushRateMBperS);
	}

	private void WritePage(long pageNumber, FileStream fileStream) {
		var (fileOffset, pageSize) = DataAccessor.GetPagePositionInFile(pageNumber);
		fileStream.Seek(offset: fileOffset, SeekOrigin.Begin);
		fileStream.Write(DataAccessor.ReadBytes(fileOffset, pageSize));
	}

	// todo later: maybe could be a common implementation across the strategies that reads
	// from the DataAccessor
	public Header ReadHeader() => ReadHeader(_path);

	private static Header ReadHeader(string path) {
		using var fileStream = new FileStream(
			path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

		try {
			//read the version first
			fileStream.Seek(offset: 0, SeekOrigin.Begin);
			byte version = (byte)fileStream.ReadByte();
			if (version != Header.CurrentVersion) {
				throw new CorruptedFileException($"Unsupported version: {version}");
			}

			//then the full header
			var headerBytes = new byte[Header.Size].AsSpan();

			fileStream.Seek(offset: 0, SeekOrigin.Begin);
			var read = fileStream.Read(headerBytes);
			if (read != Header.Size) {
				throw new CorruptedFileException(
					$"File header size ({read} bytes) does not match expected header size ({Header.Size} bytes)");
			}

			return MemoryMarshal.AsRef<Header>(headerBytes);
		} catch (Exception exc) when (exc is not CorruptedFileException) {
			throw new CorruptedFileException("Failed to read the header", exc);
		}
	}

	public void WriteHeader(Header header) {
		Log.Information("Writing header and expanding file...");
		using var fileStream = new FileStream(
			_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
		var span = MemoryMarshal.CreateReadOnlySpan(ref header, 1);
		var headerBytes = MemoryMarshal.Cast<Header, byte>(span);

		// this doesn't technically guarantee that the file is zeroed, but we expect file systems
		// that could reasonably be running eventstore to do this for us because giving us other
		// peoples data would be a security problem. if some filesystem does not to this the filter
		// will be recognised as corrupt the next time it is opened.
		fileStream.SetLength(DataAccessor.FileSize);
		fileStream.Seek(offset: 0, SeekOrigin.Begin);
		fileStream.Write(headerBytes);
		fileStream.Seek(-1, SeekOrigin.End);
		fileStream.WriteByte(0);
		fileStream.FlushToDisk();
		Log.Information("Wrote header and expanded file");
	}

	private void ThrowIfDisposed() {
		if (_disposed) {
			throw new ObjectDisposedException(nameof(FileStreamPersistence));
		}
	}

	public void Dispose() {
		lock (_bitmapLock) {
			if (_disposed)
				return;

			_disposed = true;

			if (DataAccessor is not null)
				DataAccessor.Pointer = default;

			_bloomFilterMemory?.Dispose();
			_dirtyPageBitmap?.Dispose();
		}
	}
}
