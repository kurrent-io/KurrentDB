// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.CompilerServices;
using EventStore.Common.Utils;
using Serilog;

namespace EventStore.Core.DataStructures.ProbabilisticFilter;

// encapsulates access to a persistent bloom filter through a byte* and specified sizes.
// this does the necessary pointer arithmetic and bounds checking.
// no synchronisation in here.
//
// First cache line(64 bytes) contains the header and nothing else
// Subsequent cachelines contain 60 bytes of bloom filter bits then a 4 byte hash of those bits.
//
// The hashes are as granular as they are so that the hash can be 
// cheaply calculated inline with the write (since we loaded the cacheline into
// memory anyway) which guarantees, since there is a writer lock, that
// the cacheline at any time only has at most one bit set that isn't hashed, which was
// important for the memory mapped persistece (since the OS can flush it at any time)
public unsafe class BloomFilterAccessor {
	public delegate void OnPageDirty(long pageNumber);

	public const long MinSizeKB = 10;
	public const long MaxSizeKB = 4_000_000;
	private readonly OnPageDirty _onPageDirty;
	private readonly ILogger _log;

	public BloomFilterAccessor(
		long logicalFilterSize,
		int cacheLineSize,
		int hashSize,
		int pageSize,
		OnPageDirty onPageDirty,
		ILogger log) {

		Ensure.Positive(logicalFilterSize, nameof(logicalFilterSize));
		Ensure.Positive(cacheLineSize, nameof(cacheLineSize));
		Ensure.Positive(hashSize, nameof(hashSize));
		Ensure.Positive(pageSize, nameof(pageSize));

		if (logicalFilterSize < MinSizeKB * 1000 || logicalFilterSize > MaxSizeKB * 1000) {
			throw new ArgumentOutOfRangeException(
				nameof(logicalFilterSize),
				logicalFilterSize,
				$"size should be between {MinSizeKB:N0} KB and {MaxSizeKB:N0} KB inclusive");
		}

		if (Header.Size > cacheLineSize)
			throw new Exception("header needs to fit in a cache line");

		if (pageSize % cacheLineSize != 0)
			throw new ArgumentException(
				$"PageSize {pageSize} must be a multiple of CacheLineSize {cacheLineSize}");

		LogicalFilterSize = logicalFilterSize;
		LogicalFilterSizeBits = logicalFilterSize * 8;
		CacheLineSize = cacheLineSize;
		HashSize = hashSize;
		PageSize = pageSize;
		_onPageDirty = onPageDirty;
		_log = log;

		// add room for filter bits, rounded down to nearest cacheline
		FileSize = GetBytePositionInFile(LogicalFilterSizeBits).RoundDownToMultipleOf(cacheLineSize);
		// with an extra cacheline so we have at least as much logical filter space as asked for
		// can't change this to a call to .RoundUpToMultipleOf without breaking the existing filters.
		FileSize += cacheLineSize;

		// last page may be a partial page
		NumPages = FileSize / PageSize;
		if (FileSize % PageSize != 0)
			NumPages++;
	}

	public long LogicalFilterSize { get; }
	public long LogicalFilterSizeBits { get; }
	public int CacheLineSize { get; }
	public int HashSize { get; }
	public int PageSize { get; }
	public long FileSize { get; }

	// last page may be a partial page
	public long NumPages { get; }

	// FileSize is definitely a multiple of CacheLineSize
	public long NumCacheLines => FileSize / CacheLineSize;

	// Pointer to the beginning of the file
	private byte* _pointer;
	public byte* Pointer {
		get => _pointer;
		set {
			if ((long)value % CacheLineSize != 0)
				throw new InvalidOperationException($"Pointer {(long)value} is not aligned to a cacheline ({CacheLineSize})");

			_pointer = value;
		}
	}

	// converts a bit position in the logical filter into a byte position in the file
	public long GetBytePositionInFile(long bitPosition) {
		var bytePositionInFile = bitPosition / 8;

		// account for 4 bytes at the end of each cache line being a hash
		bytePositionInFile += bytePositionInFile / (CacheLineSize - HashSize) * HashSize;

		// first cache line is reserved for the header
		bytePositionInFile += CacheLineSize;

		return bytePositionInFile;
	}

	// converts page number into byte position of the page in the file
	// the pages are aligned with the beginning of the file so that they are aligned on disk
	// the first page is therefore slightly smaller to account for the header cache line.
	public (long PositionInFile, int PageSize) GetPagePositionInFile(long pageNumber) {
		if (pageNumber >= NumPages)
			throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, "");

		var positionInFile = pageNumber * PageSize;

		var pageSize = Math.Min(PageSize, FileSize - positionInFile);

		if (pageNumber == 0) {
			pageSize -= CacheLineSize;
			positionInFile += CacheLineSize;
		}

		return (positionInFile, (int)pageSize);
	}

	// determines which page a bytePosition in the file is for.
	public long GetPageNumber(long bytePositionInFile) {
		return bytePositionInFile / PageSize;
	}

	private void EnsureBounds(long bytePosition, int length) {
		if (bytePosition > FileSize - length) {
			throw new ArgumentOutOfRangeException(
				nameof(bytePosition),
				bytePosition,
				$"Position {bytePosition:N0} + length {length:N0} = {bytePosition + length:N0} out of file size {FileSize:N0}");
		}
	}

	private ref byte ReadByte(long bytePositionInFile) {
		EnsureBounds(bytePositionInFile, length: 1);
		return ref *(Pointer + bytePositionInFile);
	}

	private Span<byte> ReadCacheLineFor(long bytePositionInFile) {
		bytePositionInFile = bytePositionInFile.RoundDownToMultipleOf(CacheLineSize);
		return ReadBytes(bytePositionInFile, CacheLineSize);
	}

	public Span<byte> ReadBytes(long bytePositionInFile, int count) {
		EnsureBounds(bytePositionInFile, count);
		return new Span<byte>(Pointer + bytePositionInFile, count);
	}

	public bool IsBitSet(long bitPosition) {
		var bytePositionInFile = GetBytePositionInFile(bitPosition);
		var byteValue = ReadByte(bytePositionInFile);
		return byteValue.IsBitSet(bitPosition % 8);
	}

	public void SetBit(long bitPosition) {
		var bytePositionInFile = GetBytePositionInFile(bitPosition);
		ref var byteValue = ref ReadByte(bytePositionInFile);
		byte oldByte = byteValue;
		byteValue = byteValue.SetBit(bitPosition % 8);

		if (byteValue != oldByte) {
			var cacheLine = ReadCacheLineFor(bytePositionInFile);

			// it would be possible to delegate the hash calculation call to the persistence
			// strategy. memmap would calculate it synchronously, filestream could defer
			// it until flush (and wouldn't even need to set it in the memory area, only
			// in the file. not worth the effort at the moment, however.
			BloomFilterIntegrity.WriteHash(cacheLine);

			if (_onPageDirty is not null) {
				var pageNumber = GetPageNumber(bytePositionInFile);
				_onPageDirty(pageNumber);
			}
		}
	}

	public void FillWithZeros() {
		_log.Debug("Zeroing bloom filter...");
		var bytesToClear = FileSize;
		var startAddress = Pointer;
		while (bytesToClear > 0) {
			uint bytesToClearInBlock = bytesToClear > uint.MaxValue
				? uint.MaxValue
				: (uint)bytesToClear;
			Unsafe.InitBlock(ref *startAddress, 0, bytesToClearInBlock);
			startAddress += bytesToClearInBlock;
			bytesToClear -= bytesToClearInBlock;
		}
		_log.Debug("Done zeroing bloom filter");
	}

	// corruptionThreshold = 0.05 => tolerate up to 5% corruption
	public void Verify(int corruptionRebuildCount, double corruptionThreshold) {
		Ensure.Nonnegative(corruptionThreshold, nameof(corruptionThreshold));

		_log.Debug("Verifying bloom filter...");
		var corruptedCacheLines = 0;

		for (long i = CacheLineSize; i < FileSize; i += CacheLineSize) {
			var cacheLine = ReadCacheLineFor(i);
			if (!BloomFilterIntegrity.ValidateHash(cacheLine)) {
				corruptedCacheLines++;
				_log.Verbose("Invalid cache line starting at {i:N0} / {fileSize:N0}", i, FileSize);
			}
		}

		var corruptionThresholdPercent = corruptionThreshold * 100;
		var corruptedPercent = (double)corruptedCacheLines / NumCacheLines * 100;
		if (corruptedPercent > corruptionThreshold * 100)
			throw new CorruptedHashException(
				corruptionRebuildCount,
				$"{corruptedCacheLines:N0} corruption(s) detected ({corruptedPercent:N2}%). Threshold {corruptionThresholdPercent:N2}%");

		if (corruptedCacheLines == 0) {
			_log.Debug("Done verifying bloom filter");
		} else {
			_log.Warning("Done verifying bloom filter: {corruptedCacheLines:N0} corruption(s) detected ({corruptedPercent:N2}%). Threshold {corruptionThresholdPercent:N2}%",
				corruptedCacheLines,
				corruptedPercent,
				corruptionThresholdPercent);
		}
	}
}
