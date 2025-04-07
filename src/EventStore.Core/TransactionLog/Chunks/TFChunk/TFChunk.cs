// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Buffers;
using DotNext.Collections.Concurrent;
using DotNext.Diagnostics;
using DotNext.IO;
using DotNext.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Exceptions;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.Transforms.Identity;
using EventStore.Core.Util;
using EventStore.Plugins.Transforms;
using Microsoft.Win32.SafeHandles;
using ILogger = Serilog.ILogger;
using MD5 = EventStore.Core.Hashing.MD5;

namespace EventStore.Core.TransactionLog.Chunks.TFChunk;

public unsafe partial class TFChunk : IDisposable {
	public enum ChunkVersions : byte {
		OriginalNotUsed = 1,
		Unaligned = 2,
		Aligned = 3,
		Transformed = 4,
	}

	public const byte CurrentChunkVersion = (byte)ChunkVersions.Transformed;
	private const int AlignmentSize = 4096;

	private static readonly ILogger Log = Serilog.Log.ForContext<TFChunk>();

	public bool IsReadOnly {
		get { return Interlocked.CompareExchange(ref _isReadOnly, 0, 0) == 1; }
		set { Interlocked.Exchange(ref _isReadOnly, value ? 1 : 0); }
	}

	public bool IsCached {
		get { return _cacheStatus == CacheStatus.Cached; }
	}

	// the logical size of (untransformed) data (could be > PhysicalDataSize if scavenged chunk)
	public long LogicalDataSize {
		get { return Interlocked.Read(ref _logicalDataSize); }
	}

	// the physical size of (untransformed) data
	public int PhysicalDataSize {
		get { return _physicalDataSize; }
	}

	public string FileName {
		get { return _filename; }
	}

	public int FileSize {
		get { return _fileSize; }
	}

	public ChunkHeader ChunkHeader {
		get { return _chunkHeader; }
	}

	public ChunkFooter ChunkFooter {
		get { return _chunkFooter; }
	}

	public ReadOnlyMemory<byte> TransformHeader {
		get { return _transformHeader; }
	}

	public readonly int MidpointsDepth;

	public int RawWriterPosition {
		get {
			return (int)(_writerWorkItem?.WorkingStream.Position
				?? throw new InvalidOperationException(string.Format("TFChunk {0} is not in write mode.", _filename)));
		}
	}

	private readonly bool _inMem;
	private readonly string _filename;
	private SafeFileHandle _handle;
	private int _fileSize;

	// This field establishes happens-before relationship with _fileStreams as follows:
	// if _isReadOnly is not 0, then _fileStreams fully initialized
	private int _isReadOnly;
	private ChunkHeader _chunkHeader;
	private ChunkFooter _chunkFooter;

	private ReaderWorkItemPool _fileStreams;
	private ReaderWorkItemPool _memStreams;

	// This field established happens-before relationship with _memStreams as follows:
	// if _memStreams has at least one available item in the pool then _sharedMemStream != null
	private Stream _sharedMemStream;
	private int _fileStreamCount;
	private int _memStreamCount;
	private int _cleanedUpFileStreams;

	private WriterWorkItem _writerWorkItem;
	private long _logicalDataSize;
	private volatile int _physicalDataSize;

	// the lock protects all three parts of the caching process:
	// CacheInMemory, UnCacheFromMemory and TryDestructMemStreams
	// so that the variables _cacheStatus, _cachedData, and _cachedLength are synchronized by the lock,
	// and so is the creation and removal of the mem readers.
	// previously TryDestructMemStreams could run concurrently with CacheInMemory,
	// potentially causing problems.
	private readonly object _cachedDataLock = new();
	private volatile nint _cachedData;
	// When the chunk is Cached/Uncaching, _cachedDataTransformed indicates whether _cachedData has had the transformation applied
	private bool _cachedDataTransformed;
	private int _cachedLength;
	private volatile CacheStatus _cacheStatus;

	private enum CacheStatus {
		// The default state.
		// CacheInMemory can transition us to Cached
		// invariants: _cachedData == IntPtr.Zero, _memStreamCount == 0
		Uncached = 0,

		// UnCacheFromMemory can transition us to Uncaching
		// invariants: _cachedData != IntPtr.Zero, _memStreamCount > 0
		Cached,

		// TryDestructMemStreams can transition us to Uncached
		// The chunk is still cached but the process of uncaching has been started by
		// UnCacheFromMemory. We are waiting for readers to be returned.
		// invariants: _cachedData != IntPtr.Zero, _memStreamCount > 0
		Uncaching,
	}

	private readonly ManualResetEventSlim _destroyEvent = new ManualResetEventSlim(false);
	private volatile bool _selfdestructin54321;
	private volatile bool _deleteFile;
	private readonly bool _unbuffered;
	private readonly bool _writeThrough;

	// https://learn.microsoft.com/en-US/troubleshoot/windows-server/application-management/operating-system-performance-degrades
	private readonly bool _reduceFileCachePressure;

	private IChunkReadSide _readSide;

	private IChunkTransform _transform;
	private ReadOnlyMemory<byte> _transformHeader;

	private TFChunk(string filename,
		int midpointsDepth,
		bool inMem,
		bool unbuffered,
		bool writethrough,
		bool reduceFileCachePressure) {
		Ensure.NotNullOrEmpty(filename, "filename");
		Ensure.Nonnegative(midpointsDepth, "midpointsDepth");

		_filename = filename;
		MidpointsDepth = midpointsDepth;
		_inMem = inMem;
		_unbuffered = unbuffered;
		_writeThrough = writethrough;
		_reduceFileCachePressure = reduceFileCachePressure;
		_memStreams = new();
		_fileStreams = new();
	}

	~TFChunk() {
		FreeCachedData();
	}

	public static TFChunk FromCompletedFile(string filename, bool verifyHash, bool unbufferedRead,
		ITransactionFileTracker tracker, Func<TransformType, IChunkTransformFactory> getTransformFactory,
		bool optimizeReadSideCache = false, bool reduceFileCachePressure = false) {
		var chunk = new TFChunk(filename,
			TFConsts.MidpointsDepth, false, unbufferedRead, false, reduceFileCachePressure);
		try {
			chunk.InitCompleted(verifyHash, optimizeReadSideCache, tracker, getTransformFactory);
		} catch {
			chunk.Dispose();
			throw;
		}

		return chunk;
	}

	public static TFChunk FromOngoingFile(string filename, int writePosition, bool unbuffered,
		bool writethrough, bool reduceFileCachePressure, ITransactionFileTracker tracker,
		Func<TransformType, IChunkTransformFactory> getTransformFactory) {
		var chunk = new TFChunk(filename,
			TFConsts.MidpointsDepth,
			false,
			unbuffered,
			writethrough,
			reduceFileCachePressure);
		try {
			chunk.InitOngoing(writePosition, tracker, getTransformFactory);
		} catch {
			chunk.Dispose();
			throw;
		}

		return chunk;
	}

	public static TFChunk CreateNew(string filename,
		int chunkDataSize,
		int chunkStartNumber,
		int chunkEndNumber,
		bool isScavenged,
		bool inMem,
		bool unbuffered,
		bool writethrough,
		bool reduceFileCachePressure,
		ITransactionFileTracker tracker,
		IChunkTransformFactory transformFactory) {
		var version = CurrentChunkVersion;
		var minCompatibleVersion = transformFactory.Type == TransformType.Identity
			? (byte)ChunkVersions.Aligned
			: version;

		var chunkHeader = new ChunkHeader(version, minCompatibleVersion, chunkDataSize, chunkStartNumber, chunkEndNumber,
			isScavenged, Guid.NewGuid(), transformFactory.Type);
		var fileSize = GetAlignedSize(transformFactory.TransformDataPosition(chunkDataSize) + ChunkHeader.Size + ChunkFooter.Size);

		return CreateWithHeader(filename, chunkHeader, fileSize, inMem, unbuffered, writethrough,
			reduceFileCachePressure, tracker, transformFactory, transformFactory.CreateTransformHeader());
	}

	public static TFChunk CreateWithHeader(string filename,
		ChunkHeader header,
		int fileSize,
		bool inMem,
		bool unbuffered,
		bool writethrough,
		bool reduceFileCachePressure,
		ITransactionFileTracker tracker,
		IChunkTransformFactory transformFactory,
		ReadOnlyMemory<byte> transformHeader) {
		var chunk = new TFChunk(filename,
			TFConsts.MidpointsDepth,
			inMem,
			unbuffered,
			writethrough,
			reduceFileCachePressure);
		try {
			chunk.InitNew(header, fileSize, tracker, transformFactory, transformHeader);
		} catch {
			chunk.Dispose();
			throw;
		}

		return chunk;
	}

	private void InitCompleted(bool verifyHash, bool optimizeReadSideCache, ITransactionFileTracker tracker,
		Func<TransformType, IChunkTransformFactory> getTransformFactory) {
		var fileInfo = new FileInfo(_filename);
		if (!fileInfo.Exists)
			throw new CorruptDatabaseException(new ChunkNotFoundException(_filename));

		_fileSize = (int)fileInfo.Length;

		_handle = File.OpenHandle(
			_filename,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite,
			_reduceFileCachePressure ? FileOptions.None : FileOptions.RandomAccess);

		IsReadOnly = true;
		SetAttributes(_filename, true);

		using (var stream = _handle.AsUnbufferedStream(FileAccess.Read)) {
			_chunkHeader = ReadHeader(stream);
			Log.Debug("Opened completed {chunk} as version {version} (min. compatible version: {minCompatibleVersion})", _filename, _chunkHeader.Version, _chunkHeader.MinCompatibleVersion);

			if (_chunkHeader.MinCompatibleVersion > CurrentChunkVersion)
				throw new CorruptDatabaseException(new UnsupportedFileVersionException(_filename, _chunkHeader.MinCompatibleVersion,
					CurrentChunkVersion));

			var transformFactory = getTransformFactory(_chunkHeader.TransformType);
			_transformHeader = transformFactory.ReadTransformHeader(stream);
			_transform = transformFactory.CreateTransform(_transformHeader);

			_chunkFooter = ReadFooter(stream);
			if (!_chunkFooter.IsCompleted) {
				throw new CorruptDatabaseException(new BadChunkInDatabaseException(
					string.Format("Chunk file '{0}' should be completed, but is not.", _filename)));
			}

			_logicalDataSize = _chunkFooter.LogicalDataSize;
			_physicalDataSize = _chunkFooter.PhysicalDataSize;
		}

		CreateReaderStreams();

		_readSide = _chunkHeader.IsScavenged
			? new TFChunkReadSideScavenged(this, optimizeReadSideCache, tracker)
			: new TFChunkReadSideUnscavenged(this, tracker);

		// do not actually cache now because it is too slow when opening the database
		_readSide.RequestCaching();

		if (verifyHash)
			VerifyFileHash();
	}

	private void InitNew(ChunkHeader chunkHeader, int fileSize, ITransactionFileTracker tracker,
		IChunkTransformFactory transformFactory, ReadOnlyMemory<byte> transformHeader) {
		Ensure.NotNull(chunkHeader, "chunkHeader");
		Ensure.Positive(fileSize, "fileSize");

		_fileSize = fileSize;
		IsReadOnly = false;
		_chunkHeader = chunkHeader;
		_physicalDataSize = 0;
		_logicalDataSize = 0;
		_transformHeader = transformHeader;
		_transform = transformFactory.CreateTransform(_transformHeader);

		if (_inMem)
			CreateInMemChunk(chunkHeader, fileSize, transformHeader);
		else {
			CreateWriterWorkItemForNewChunk(chunkHeader, fileSize, transformHeader);
			SetAttributes(_filename, false);
		}

		_readSide = chunkHeader.IsScavenged
			? new TFChunkReadSideScavenged(this, false, tracker)
			: new TFChunkReadSideUnscavenged(this, tracker);

		// Always cache the active chunk
		// If the chunk is scavenged we will definitely mark it readonly before we are done writing to it.
		if (!chunkHeader.IsScavenged) {
			CacheInMemory();
		}
	}

	private void InitOngoing(int writePosition, ITransactionFileTracker tracker,
		Func<TransformType, IChunkTransformFactory> getTransformFactory) {
		Ensure.Nonnegative(writePosition, "writePosition");
		var fileInfo = new FileInfo(_filename);
		if (!fileInfo.Exists)
			throw new CorruptDatabaseException(new ChunkNotFoundException(_filename));

		_fileSize = (int)fileInfo.Length;
		IsReadOnly = false;
		_physicalDataSize = writePosition;
		_logicalDataSize = writePosition;

		SetAttributes(_filename, false);
		CreateWriterWorkItemForExistingChunk(writePosition, getTransformFactory, out _chunkHeader);
		Log.Debug("Opened ongoing {chunk} as version {version} (min. compatible version: {minCompatibleVersion})", _filename, _chunkHeader.Version, _chunkHeader.MinCompatibleVersion);

		if (_chunkHeader.MinCompatibleVersion > CurrentChunkVersion)
			throw new CorruptDatabaseException(new UnsupportedFileVersionException(_filename, _chunkHeader.MinCompatibleVersion,
				CurrentChunkVersion));

		_readSide = new TFChunkReadSideUnscavenged(this, tracker);

		// Always cache the active chunk
		CacheInMemory();
	}

	// If one file stream writes to a file, and another file stream happens to have that part of
	// the same file already in its buffer, the buffer is not (any longer) invalidated and a read from
	// the second file stream will not contain the write.
	// We therefore only read from memory while the chunk is still being written to, and only create
	// the file streams when the chunk is being completed.
	private void CreateReaderStreams() {
		_fileStreams.Reuse();
		Interlocked.Add(ref _fileStreamCount, IndexPool.Capacity);
	}

	private void CreateInMemChunk(ChunkHeader chunkHeader, int fileSize, ReadOnlyMemory<byte> transformHeader) {
		var md5 = MD5.Create();

		// ALLOCATE MEM
		_cacheStatus = CacheStatus.Cached;
		_cachedLength = fileSize;
		_cachedData = Marshal.AllocHGlobal(_cachedLength);
		_cachedDataTransformed = true;
		GC.AddMemoryPressure(_cachedLength);


		// WRITE HEADERS
		using (var headerStream = new UnmanagedMemoryStream((byte*)_cachedData, _cachedLength, _cachedLength, FileAccess.ReadWrite)) {
			WriteHeader(md5, headerStream, chunkHeader);
			WriteTransformHeader(md5, headerStream, transformHeader);
		}

		// WRITER STREAM
		var writerWorkItem = new WriterWorkItem(_cachedData, _cachedLength, md5, _transform.Write, ChunkHeader.Size + transformHeader.Length);

		// READER STREAMS
		_sharedMemStream = CreateSharedMemoryStream();
		Interlocked.Add(ref _memStreamCount, IndexPool.Capacity);
		_memStreams.Reuse();

		// should never happen in practice because this function is called from the static TFChunk constructors
		Debug.Assert(!_selfdestructin54321);

		_writerWorkItem = writerWorkItem;
	}

	private Stream CreateSharedMemoryStream() {
		Debug.Assert(_cachedData is not 0);
		Debug.Assert(_cachedLength > 0);

		ReadOnlyMemory<byte> memoryView = UnmanagedMemory.AsMemory((byte*)_cachedData, _cachedLength);
		return StreamSource.AsSharedStream(new(memoryView), compatWithAsync: false);
	}

	private FileOptions WritableHandleOptions {
		get {
			var options = _reduceFileCachePressure ? FileOptions.None : FileOptions.RandomAccess;
			if (_writeThrough)
				options |= FileOptions.WriteThrough;

			return options;
		}
	}

	private void CreateWriterWorkItemForNewChunk(ChunkHeader chunkHeader, int fileSize, ReadOnlyMemory<byte> transformHeader) {
		var md5 = MD5.Create();

		// create temp file first and set desired length
		// if there is not enough disk space or something else prevents file to be resized as desired
		// we'll end up with empty temp file, which won't trigger false error on next DB verification
		var tempFilename = string.Format("{0}.{1}.tmp", _filename, Guid.NewGuid());
		var options = new FileStreamOptions {
			Mode = FileMode.CreateNew,
			Access = FileAccess.ReadWrite,
			Share = FileShare.Read,
			Options = FileOptions.SequentialScan,
			PreallocationSize = fileSize, // avoid fragmentation of file
			BufferSize = WriterWorkItem.BufferSize,
		};

		var tempFile = new FileStream(tempFilename, options);
		tempFile.SetLength(fileSize);

		// we need to write header into temp file before moving it into correct chunk place, so in case of crash
		// we don't end up with seemingly valid chunk file with no header at all...
		WriteHeader(md5, tempFile, chunkHeader);
		WriteTransformHeader(md5, tempFile, transformHeader);

		tempFile.FlushToDisk();
		tempFile.Close();
		File.Move(tempFilename, _filename);

		_handle = File.OpenHandle(_filename, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, WritableHandleOptions);
		_writerWorkItem = new(_handle, md5, _unbuffered, _transform.Write, ChunkHeader.Size + transformHeader.Length);
		Flush(); // persist file move result
	}

	private void CreateWriterWorkItemForExistingChunk(int writePosition,
		Func<TransformType, IChunkTransformFactory> getTransformFactory, out ChunkHeader chunkHeader) {
		_handle = File.OpenHandle(_filename, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, WritableHandleOptions);

		try {
			using var stream = _handle.AsUnbufferedStream(FileAccess.ReadWrite);
			chunkHeader = ReadHeader(stream);
			if (chunkHeader.Version == (byte)ChunkVersions.Unaligned) {
				Log.Debug("Upgrading ongoing file {chunk} to version 3", _filename);
				var newHeader = new ChunkHeader((byte)ChunkVersions.Aligned,
					(byte)ChunkVersions.Aligned,
					chunkHeader.ChunkSize,
					chunkHeader.ChunkStartNumber,
					chunkHeader.ChunkEndNumber,
					false,
					chunkHeader.ChunkId,
					chunkHeader.TransformType);
				stream.Seek(0, SeekOrigin.Begin);
				chunkHeader = newHeader;
				var head = newHeader.AsByteArray();
				stream.Write(head, 0, head.Length);
				stream.Flush();
			}

			var transformFactory = getTransformFactory(chunkHeader.TransformType);
			_transformHeader = transformFactory.ReadTransformHeader(stream);
			_transform = transformFactory.CreateTransform(_transformHeader);
		} catch {
			_handle.Dispose();
			throw;
		}

		var workItem = new WriterWorkItem(_handle, MD5.Create(), _unbuffered, _transform.Write, 0);
		var realPosition = GetRawPosition(writePosition);
		// the writer work item's stream is responsible for computing the current checksum when the position is set
		workItem.WorkingStream.Position = realPosition;
		_writerWorkItem = workItem;
	}

	private void WriteHeader(HashAlgorithm md5, Stream stream, ChunkHeader chunkHeader) {
		var chunkHeaderBytes = chunkHeader.AsByteArray();
		md5.TransformBlock(chunkHeaderBytes, 0, ChunkHeader.Size, null, 0);
		stream.Write(chunkHeaderBytes, 0, ChunkHeader.Size);
	}

	private void WriteTransformHeader(HashAlgorithm md5, Stream stream, ReadOnlyMemory<byte> transformHeader) {
		if (transformHeader.IsEmpty)
			return;

		var transformHeaderBytes = transformHeader.ToArray();
		md5.TransformBlock(transformHeaderBytes, 0, transformHeaderBytes.Length, null, 0);
		stream.Write(transformHeaderBytes);
	}

	private void SetAttributes(string filename, bool isReadOnly) {
		if (_inMem)
			return;
		// in mono SetAttributes on non-existing file throws exception, in windows it just works silently.
		Helper.EatException(() => {
			if (isReadOnly)
				File.SetAttributes(filename, FileAttributes.ReadOnly | FileAttributes.NotContentIndexed);
			else
				File.SetAttributes(filename, FileAttributes.NotContentIndexed);
		});
	}

	public void VerifyFileHash() {
		if (!IsReadOnly)
			throw new InvalidOperationException("You can't verify hash of not-completed TFChunk.");

		Log.Debug("Verifying hash for TFChunk '{chunk}'...", _filename);
		using (var reader = AcquireRawReader()) {
			reader.Stream.Seek(0, SeekOrigin.Begin);
			var stream = reader.Stream;
			var footer = _chunkFooter;

			byte[] hash;
			using (var md5 = MD5.Create()) {
				// hash whole chunk except MD5 hash sum which should always be last
				MD5Hash.ContinuousHashFor(md5, stream, 0, _fileSize - ChunkFooter.ChecksumSize);
				md5.TransformFinalBlock(Empty.ByteArray, 0, 0);
				hash = md5.Hash;
			}

			if (footer.MD5Hash == null || footer.MD5Hash.Length != hash.Length)
				throw new HashValidationException();

			for (int i = 0; i < hash.Length; ++i) {
				if (footer.MD5Hash[i] != hash[i])
					throw new HashValidationException();
			}
		}
	}

	private ChunkHeader ReadHeader(Stream stream) {
		if (stream.Length < ChunkHeader.Size) {
			throw new CorruptDatabaseException(new BadChunkInDatabaseException(
				string.Format("Chunk file '{0}' is too short to even read ChunkHeader, its size is {1} bytes.",
					_filename,
					stream.Length)));
		}

		stream.Seek(0, SeekOrigin.Begin);
		var chunkHeader = ChunkHeader.FromStream(stream);
		return chunkHeader;
	}

	private ChunkFooter ReadFooter(Stream stream) {
		if (stream.Length < ChunkFooter.Size) {
			throw new CorruptDatabaseException(new BadChunkInDatabaseException(
				string.Format("Chunk file '{0}' is too short to even read ChunkFooter, its size is {1} bytes.",
					_filename,
					stream.Length)));
		}

		try {
			stream.Seek(-ChunkFooter.Size, SeekOrigin.End);
			var footer = ChunkFooter.FromStream(stream);
			return footer;
		} catch (Exception ex) {
			throw new Exception("error in chunk file " + _filename, ex);
		}
	}

	private static long GetRawPosition(long logicalPosition) {
		return ChunkHeader.Size + logicalPosition;
	}

	private static long GetDataPosition(WriterWorkItem workItem) {
		return workItem.WorkingStream.Position - ChunkHeader.Size;
	}

	// There are four kinds of event position
	// (a) global logical (logical position in the log)
	// (b) local logical (logical position in the chunk, which is global logical - chunk logical start)
	// (c) actual (local logical but with posmap taken into account)
	// (d) raw (byte offset in file, which is actual - header size)
	//
	// this method takes (b) and returns (d)
	public long GetActualRawPosition(long logicalPosition) {
		ArgumentOutOfRangeException.ThrowIfNegative(logicalPosition);

		var actualPosition = _readSide.GetActualPosition(logicalPosition);

		if (actualPosition < 0)
			return -1;

		return GetRawPosition(actualPosition);
	}

	public void CacheInMemory() {
		if (_inMem)
			return;

		lock (_cachedDataLock) {
			if (_cacheStatus != CacheStatus.Uncached) {
				// expected to be very rare
				if (_cacheStatus == CacheStatus.Uncaching)
					Log.Debug("CACHING TFChunk {chunk} SKIPPED because it is uncaching.", this);

				return;
			}

			// we won the right to cache
			var sw = new Timestamp();
			try {
				// note: we do not want to cache transformed data for the active chunk as we would be incurring the cost of
				// transformation twice - once when writing to the filestream and once when writing to the memory stream.
				// however, we want to cache (already) transformed data for completed/read-only chunks as we would otherwise
				// incur the cost of transforming the whole chunk when loading data from the file into memory.

				if (!IsReadOnly)
					// we do not cache the header for the active chunk -
					// it's not necessary as the cache is used only for reading data.
					BuildCacheArray(
						size: GetAlignedSize(ChunkHeader.Size + _chunkHeader.ChunkSize + ChunkFooter.Size),
						reader: AcquireFileReader(raw: false),
						offset: ChunkHeader.Size,
						count: _physicalDataSize,
						transformed: false);
				else
					BuildCacheArray(
						size: _fileSize,
						reader: AcquireFileReader(raw: true),
						offset: 0,
						count: _fileSize,
						transformed: true);
			} catch (OutOfMemoryException) {
				Log.Error("CACHING FAILED due to OutOfMemory exception in TFChunk {chunk}.", this);
				return;
			} catch (FileBeingDeletedException) {
				Log.Debug(
					"CACHING FAILED due to FileBeingDeleted exception (TFChunk is being disposed) in TFChunk {chunk}.",
					this);
				return;
			}

			_sharedMemStream = CreateSharedMemoryStream();
			Interlocked.Add(ref _memStreamCount, IndexPool.Capacity);
			_memStreams.Reuse();

			if (_selfdestructin54321) {
				if (Interlocked.Add(ref _memStreamCount, -IndexPool.Capacity) == 0)
					FreeCachedData();
				Log.Debug("CACHING ABORTED for TFChunk {chunk} as TFChunk was probably marked for deletion.", this);
				return;
			}

			if (_writerWorkItem is { } writerWorkItem) {
				UnmanagedMemoryStream memStream = new((byte*)_cachedData, _cachedLength, _cachedLength, FileAccess.ReadWrite) {
					Position = writerWorkItem.WorkingStream.Position,
				};
				writerWorkItem.SetMemStream(memStream);
			}

			_readSide.Uncache();

			Log.Debug("CACHED TFChunk {chunk} in {elapsed}.", this, sw.Elapsed);

			if (_selfdestructin54321)
				TryDestructMemStreams();

			_cacheStatus = CacheStatus.Cached;
		}
	}

	private void BuildCacheArray(int size, TFChunkBulkReader reader, int offset, int count, bool transformed) {
		try {
			if (reader.IsMemory)
				throw new InvalidOperationException(
					"When trying to build cache, reader worker is already in-memory reader.");

			_cachedLength = size;
			var cachedData = Marshal.AllocHGlobal(_cachedLength);
			GC.AddMemoryPressure(_cachedLength);

			try {
				Span<byte> memoryView = new(IntPtr.Add(cachedData, offset).ToPointer(), count);
				reader.Stream.Seek(offset, SeekOrigin.Begin);
				reader.Stream.ReadExactly(memoryView);
			} catch {
				Marshal.FreeHGlobal(cachedData);
				GC.RemoveMemoryPressure(_cachedLength);
				throw;
			}

			_cachedData = cachedData;
			_cachedDataTransformed = transformed;
		} finally {
			reader.Dispose();
		}
	}

	public void UnCacheFromMemory() {
		lock (_cachedDataLock) {
			if (_inMem)
				return;
			if (_cacheStatus == CacheStatus.Cached) {
				// we won the right to un-cache and chunk was cached
				// possibly we could use a mem reader work item and do the actual midpoint caching now
				_readSide.RequestCaching();

				_writerWorkItem?.DisposeMemStream();

				Log.Debug("UNCACHING TFChunk {chunk}.", this);
				_cacheStatus = CacheStatus.Uncaching;
				// this memory barrier corresponds to the barrier in ReturnReaderWorkItem
				Thread.MemoryBarrier();
				TryDestructMemStreams();
			}
		}
	}

	public bool ExistsAt(long logicalPosition) {
		return _readSide.ExistsAt(logicalPosition);
	}

	public void OptimizeExistsAt() {
		if (!ChunkHeader.IsScavenged)
			return;
		((TFChunkReadSideScavenged)_readSide).OptimizeExistsAt();
	}

	public void DeOptimizeExistsAt() {
		if (!ChunkHeader.IsScavenged)
			return;
		((TFChunkReadSideScavenged)_readSide).DeOptimizeExistsAt();
	}

	public RecordReadResult TryReadAt(long logicalPosition, bool couldBeScavenged) {
		return _readSide.TryReadAt(logicalPosition, couldBeScavenged);
	}

	public async ValueTask<RecordReadResult> TryReadFirst(CancellationToken token) {
		token.ThrowIfCancellationRequested();
		return _readSide.TryReadFirst();
	}

	public RecordReadResult TryReadClosestForward(long logicalPosition) {
		return _readSide.TryReadClosestForward(logicalPosition);
	}

	public RawReadResult TryReadClosestForwardRaw(long logicalPosition, Func<int, byte[]> getBuffer) {
		return _readSide.TryReadClosestForwardRaw(logicalPosition, getBuffer);
	}

	public RecordReadResult TryReadLast() {
		return _readSide.TryReadLast();
	}

	public RecordReadResult TryReadClosestBackward(long logicalPosition) {
		return _readSide.TryReadClosestBackward(logicalPosition);
	}

	public RecordWriteResult TryAppend(ILogRecord record) {
		if (IsReadOnly)
			throw new InvalidOperationException("Cannot write to a read-only block.");

		var workItem = _writerWorkItem;
		var buffer = workItem.Buffer;
		var bufferWriter = workItem.BufferWriter;

		buffer.SetLength(4);
		buffer.Position = 4;
		record.WriteTo(bufferWriter);
		var length = (int)buffer.Length - 4;
		bufferWriter.Write(length); // length suffix
		buffer.Position = 0;
		bufferWriter.Write(length); // length prefix

		if (workItem.WorkingStream.Position + length + 2 * sizeof(int) > ChunkHeader.Size + _chunkHeader.ChunkSize)
			return RecordWriteResult.Failed(GetDataPosition(workItem));

		var oldPosition = WriteRawData(workItem, buffer);
		_physicalDataSize = (int)GetDataPosition(workItem); // should fit 32 bits
		_logicalDataSize = ChunkHeader.GetLocalLogPosition(record.LogPosition + length + 2 * sizeof(int));

		// for non-scavenged chunk _physicalDataSize should be the same as _logicalDataSize
		// for scavenged chunk _logicalDataSize should be at least the same as _physicalDataSize
		if ((!ChunkHeader.IsScavenged && _logicalDataSize != _physicalDataSize)
			|| (ChunkHeader.IsScavenged && _logicalDataSize < _physicalDataSize)) {
			throw new Exception(string.Format(
				"Data sizes violation. Chunk: {0}, IsScavenged: {1}, LogicalDataSize: {2}, PhysicalDataSize: {3}.",
				FileName, ChunkHeader.IsScavenged, _logicalDataSize, _physicalDataSize));
		}

		return RecordWriteResult.Successful(oldPosition, _physicalDataSize);
	}

	public bool TryAppendRawData(ReadOnlyMemory<byte> buffer) {
		var workItem = _writerWorkItem;
		if (workItem.WorkingStream.Position + buffer.Length > workItem.WorkingStream.Length)
			return false;
		WriteRawData(workItem, buffer);
		return true;
	}

	private static long WriteRawData(WriterWorkItem workItem, MemoryStream buffer) {
		var len = (int)buffer.Length;
		var buf = buffer.GetBuffer();
		return WriteRawData(workItem, buf.AsMemory(0, len));
	}

	private static long WriteRawData(WriterWorkItem workItem, ReadOnlyMemory<byte> buf) {
		var curPos = GetDataPosition(workItem);
		// the writer work item's stream is responsible for updating the checksum
		workItem.AppendData(buf);
		return curPos;
	}

	public void Flush() {
		if (IsReadOnly)
			return;
		_writerWorkItem.FlushToDisk();
	}

	public void Complete() {
		if (ChunkHeader.IsScavenged)
			throw new InvalidOperationException("CompleteScavenged should be used for scavenged chunks.");
		CompleteNonRaw(null);
	}

	public async ValueTask CompleteScavenge(ICollection<PosMap> mapping, CancellationToken token) {
		if (!ChunkHeader.IsScavenged)
			throw new InvalidOperationException("CompleteScavenged should not be used for non-scavenged chunks.");

		token.ThrowIfCancellationRequested();
		CompleteNonRaw(mapping);
	}

	private void CompleteNonRaw(ICollection<PosMap> mapping) {
		if (IsReadOnly)
			throw new InvalidOperationException("Cannot complete a read-only TFChunk.");

		_chunkFooter = WriteFooter(mapping);
		Flush();

		if (!_inMem)
			CreateReaderStreams();

		IsReadOnly = true;

		_writerWorkItem?.Dispose();
		_writerWorkItem = null;

		SetAttributes(_filename, true);
	}

	public async ValueTask CompleteRaw(CancellationToken token) {
		if (IsReadOnly)
			throw new InvalidOperationException("Cannot complete a read-only TFChunk.");
		if (_writerWorkItem.WorkingStream.Position != _writerWorkItem.WorkingStream.Length)
			throw new InvalidOperationException("The raw chunk is not completely written.");

		token.ThrowIfCancellationRequested();
		Flush();

		if (!_inMem)
			CreateReaderStreams();

		IsReadOnly = true;

		_writerWorkItem?.Dispose();
		_writerWorkItem = null;

		SetAttributes(_filename, true);

		if (!_inMem) {
			using var stream = _handle.AsUnbufferedStream(FileAccess.Read);
			_chunkFooter = ReadFooter(stream);
		} else {
			_chunkFooter = ReadFooter(_sharedMemStream);
		}
	}

	private ChunkFooter WriteFooter(ICollection<PosMap> mapping) {
		var workItem = _writerWorkItem;
		workItem.ResizeStream((int)workItem.WorkingStream.Position);

		int mapSize = 0;
		if (mapping != null) {
			if (_inMem)
				throw new InvalidOperationException(
					"Cannot write an in-memory chunk with a PosMap. " +
					"Scavenge is not supported on in-memory databases");

			if (_cacheStatus != CacheStatus.Uncached) {
				throw new InvalidOperationException("Trying to write mapping while chunk is cached. "
													+ "You probably are writing scavenged chunk as cached. "
													+ "Do not do this.");
			}

			mapSize = mapping.Count * PosMap.FullSize;
			workItem.Buffer.SetLength(mapSize);
			workItem.Buffer.Position = 0;
			foreach (var map in mapping) {
				map.Write(workItem.BufferWriter);
			}

			WriteRawData(workItem, workItem.Buffer);
		}

		workItem.FlushToDisk();

		_transform.Write.CompleteData(
			footerSize: ChunkFooter.Size,
			alignmentSize: _chunkHeader.Version >= (byte)ChunkVersions.Aligned ? AlignmentSize : 1);

		Flush();

		var footerNoHash = new ChunkFooter(true, true, _physicalDataSize, LogicalDataSize, mapSize,
			new byte[ChunkFooter.ChecksumSize]);
		//MD5
		workItem.MD5.TransformFinalBlock(footerNoHash.AsByteArray(), 0,
			ChunkFooter.Size - ChunkFooter.ChecksumSize);
		//FILE
		var footerWithHash =
			new ChunkFooter(true, true, _physicalDataSize, LogicalDataSize, mapSize, workItem.MD5.Hash);
		_transform.Write.WriteFooter(footerWithHash.AsByteArray(), out var fileSize);

		Flush();

		_fileSize = fileSize;
		return footerWithHash;
	}

	public void Dispose() => TryClose();

	public bool TryClose() {
		_selfdestructin54321 = true;

		Thread.MemoryBarrier();

		bool closed = true;
		closed &= TryDestructFileStreams();
		closed &= TryDestructMemStreams();

		return closed;
	}

	public void MarkForDeletion() {
		_selfdestructin54321 = true;
		_deleteFile = true;

		Thread.MemoryBarrier();

		TryDestructFileStreams();
		TryDestructMemStreams();
	}

	private bool TryDestructFileStreams() {
		switch (_fileStreams.Drain(ref _fileStreamCount)) {
			case < 0:
				throw new Exception("Count of file streams reduced below zero.");
			case 0:
				CleanUpFileStreamDestruction();
				return true;
			default:
				return false;
		}
	}

	// Called when the filestreams have all been returned and disposed.
	// This used to be a 'last one out turns off the light' mechanism, but now it is idempotent
	// so it is more like 'make sure the light is off if no one is using it'.
	// The idempotency means that
	//  1. we don't have to worry if we just disposed the last filestream or if someone else did before.
	//  2. this mechanism will work if we decide not to create any pooled filestreams at all
	//        (previously if we didnt create any filestreams then no one would call this method)
	private void CleanUpFileStreamDestruction() {
		if (Interlocked.CompareExchange(ref _cleanedUpFileStreams, 1, 0) != 0)
			return;

		if (_writerWorkItem is not null) {
			_writerWorkItem.FlushToDisk();
			_writerWorkItem.Dispose();
		}

		if (!_inMem) {
			_handle?.Dispose();
			Helper.EatException(() => File.SetAttributes(_filename, FileAttributes.Normal));

			if (_deleteFile) {
				Log.Information("File {chunk} has been marked for delete and will be deleted in TryDestructFileStreams.",
					Path.GetFileName(_filename));
				Helper.EatException(() => File.Delete(_filename));
			}
		}

		_destroyEvent.Set();
	}

	public static int GetAlignedSize(int size) {
		if (size % AlignmentSize == 0)
			return size;
		return (size / AlignmentSize + 1) * AlignmentSize;
	}

	private bool TryDestructMemStreams() {
		lock (_cachedDataLock) {
			if (_cacheStatus != CacheStatus.Uncaching && !_selfdestructin54321)
				return false;

			_writerWorkItem?.DisposeMemStream();

			switch (_memStreams.Drain(ref _memStreamCount)) {
				case < 0:
					throw new Exception("Count of memory streams reduced below zero.");
				case 0:
					// make sure "the light is off" for memory streams
					FreeCachedData();
					return true;
				default:
					return false;
			}
		}
	}

	private void FreeCachedData() {
		lock (_cachedDataLock) {
			var cachedData = _cachedData;
			if (cachedData is not 0) {
				Marshal.FreeHGlobal(cachedData);
				GC.RemoveMemoryPressure(_cachedLength);
				_cachedData = 0;
				_cachedLength = 0;
				_cacheStatus = CacheStatus.Uncached;
				Interlocked.Exchange(ref _sharedMemStream, null)?.Dispose();
				Log.Debug("UNCACHED TFChunk {chunk}.", this);
			}
		}
	}

	public void WaitForDestroy(int timeoutMs) {
		if (!_destroyEvent.Wait(timeoutMs))
			throw new TimeoutException();
	}

	private ReaderWorkItem GetReaderWorkItem() {
		if (_selfdestructin54321)
			throw new FileBeingDeletedException();

		// try get memory stream reader first
		if (_memStreams.TryTake(out var slot)) {
			// When caching, _cachedDataTransformed and _sharedMemStream are both set before
			// _memStreams.Reuse() (which repopulates the pool that was definitely empty)
			// The Interlocked.Add(_memStreamCount) barrier guarantees the order.
			// So since we have got a slot from the pool, we are guaranteed that _sharedMemStream and
			// _cachedDataTransformed are populated with the correct values and further more their
			// values will not change because we have the slot, preventing any uncaching.
			Debug.Assert(_sharedMemStream is not null);

			if (slot.ValueRef is not { } memoryWorkItem) {
				memoryWorkItem = slot.ValueRef = new(
					_sharedMemStream,
					_cachedDataTransformed ? _transform.Read : IdentityChunkReadTransform.Instance) { PositionInPool = slot.Index };
			}

			return memoryWorkItem;
		} else if (_selfdestructin54321) {
			throw new FileBeingDeletedException();
		} else if (Atomic.UpdateAndGet(ref _memStreamCount, IncrementIfGreaterThanZero) > 0) {
			// We did not get a slot from the pool, but we incremented _memStreamCount from an
			// already positive number. This means there are other mem readers in existence so we can
			// create a new one separate to the pool because their existence guarantees that
			// _sharedMemStream and _cachedDataTransformed are populated and will not change until
			// _memStreamCount returns to 0.
			//
			// If the number of mem readers in existence had dropped to 0 then the uncaching
			// procedure may be in progress and _sharedMemStream and _cachedDataTransformed may
			// change or become invalid, so we don't use them. Instead fallback to filestream.
			Debug.Assert(_sharedMemStream is not null);

			return new(_sharedMemStream, _cachedDataTransformed ? _transform.Read : IdentityChunkReadTransform.Instance);
		}

		if (!IsReadOnly) {
			// chunks cannot be read using filestreams while they can still be written to
			throw new Exception(_cacheStatus is not CacheStatus.Cached
				? "Active chunk must be cached but was not."
				: "Not enough memory streams for active chunk.");
		}

		// get a filestream from the pool, or create one if the pool is empty.
		if (_fileStreams.TryTake(out slot)) {
			if (slot.ValueRef is not { } fileStreamWorkItem)
				slot.ValueRef = fileStreamWorkItem = new(_handle, _transform.Read) { PositionInPool = slot.Index };

			return fileStreamWorkItem;
		}

		Interlocked.Increment(ref _fileStreamCount);

		if (_selfdestructin54321) {
			if (Interlocked.Decrement(ref _fileStreamCount) == 0)
				CleanUpFileStreamDestruction();
			throw new FileBeingDeletedException();
		}

		return new(_handle, _transform.Read);

		static int IncrementIfGreaterThanZero(int value)
			=> value + Unsafe.BitCast<bool, byte>(value > 0);
	}

	private void ReturnReaderWorkItem(ReaderWorkItem item) {
		if (item.IsMemory) {
			// we avoid taking the _cachedDataLock here every time because we would be
			// contending with other reader threads also returning readerworkitems.
			//
			// instead we check _cacheStatus to give us a hint about whether we need to lock.
			// but this read of _cacheStatus is not inside the lock, so it can be wrong
			// because we read a stale value, or it changed immediately after
			// we read it.
			//
			// if the check is wrong it can result in one of two outcomes:
			// 1. we call TryDestructMemStreams when we shouldn't.
			//    we protect against this by checking the condition again inside the lock
			// 2. we don't call TryDestructMemStreams when we should.
			//    the memory barrier, corresponding to the barrier in UnCacheFromMemory, protects
			//    against this by guaranteeing ordering as emphasied in _italics_ below.
			//    the case protected against is that another thread might call UnCacheFromMemory just as
			//    we are returning an item here and neither thread tidys up the item we are returning.
			//    but consider: UnCacheFromMemory sets the state to Uncaching _and then_ tidys up the
			//    items in the pool. if it does not tidy up our item, it is because our item wasn't
			//    in the pool. which means we hadn't enqueued it yet, which means that we will
			//    enqueue it and then _after that_ read _cacheStatus and find it is Uncaching,
			//    which means we will tidy it up here.
			//    this works in the same way as the barriers in ObjectPool.cs
			//
			// if we do end up needing to take the lock the risk of having to wait a long time is
			// low (or possibly none), because Caching is the only slow operation while holding
			// the lock and it only occurs when there are no outstanding memory readers, but we know
			// there is one currently because we are in the process of returning it.
			if (!_memStreams.Return(item)) {
				// item was not taken from the pool, destroy immediately
				item.Dispose();
				Interlocked.Decrement(ref _memStreamCount);
			}

			Thread.MemoryBarrier();
			if (_cacheStatus == CacheStatus.Uncaching || _selfdestructin54321)
				TryDestructMemStreams();
		} else {
			if (!_fileStreams.Return(item)) {
				// item was not taken from the pool, destroy immediately
				item.Dispose();
				Interlocked.Decrement(ref _fileStreamCount);
			}

			if (_selfdestructin54321)
				TryDestructFileStreams();
		}
	}

	public TFChunkBulkReader AcquireDataReader() {
		if (TryAcquireBulkMemReader(raw: false, out var reader))
			return reader;

		return AcquireFileReader(raw: false);
	}

	public TFChunkBulkReader AcquireRawReader() {
		if (TryAcquireBulkMemReader(raw: true, out var reader))
			return reader;

		return AcquireFileReader(raw: true);
	}

	private TFChunkBulkReader AcquireFileReader(bool raw) {
		Interlocked.Increment(ref _fileStreamCount);
		if (_selfdestructin54321) {
			if (Interlocked.Decrement(ref _fileStreamCount) == 0) {
				CleanUpFileStreamDestruction();
			}

			throw new FileBeingDeletedException();
		}

		// if we get here, then we reserved TFChunk for sure so no one should dispose of chunk file
		// until client returns dedicated reader
		var stream = CreateFileStreamForBulkReader();

		if (raw) {
			return new TFChunkBulkRawReader(this, stream, isMemory: false);
		}

		var streamToUse = _transform.Read.TransformData(new ChunkDataReadStream(stream));
		return new TFChunkBulkDataReader(this, streamToUse, isMemory: false);
	}

	private Stream CreateFileStreamForBulkReader() => _inMem
			? new UnmanagedMemoryStream((byte*)_cachedData, _fileSize)
			: new FileStream(_filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536,
				FileOptions.SequentialScan);

	// tries to acquire a bulk reader over a memstream but
	// (a) doesn't block if a file reader would be acceptable instead
	//     (we might be in the middle of caching which could take a while)
	// (b) _does_ throw if we can't get a memstream and a filestream is not acceptable
	private bool TryAcquireBulkMemReader(bool raw, out TFChunkBulkReader reader) {
		reader = null;

		if (IsReadOnly) {
			// chunk is definitely readonly and will remain so, so a filestream would be acceptable.
			// we might be able to get a memstream but we don't want to wait for the lock in case we
			// are currently performing a slow operation with it such as caching.
			if (!Monitor.TryEnter(_cachedDataLock))
				return false;

			try {
				return TryCreateBulkMemReader(raw, out reader);
			} finally {
				Monitor.Exit(_cachedDataLock);
			}
		}

		// chunk is not readonly so it should be cached and let us create a mem reader
		// (but might become readonly at any moment!)
		if (TryCreateBulkMemReader(raw, out reader))
			return true;

		// we couldn't get a memreader, maybe we just became readonly and got uncached.
		if (IsReadOnly) {
			// we did become readonly, it is acceptable to fall back to filestream.
			return false;
		} else {
			// we are not yet readonly, we shouldn't have failed to get a memstream and we
			// cannot fall back to file stream.
			throw new Exception("Failed to get a MemStream bulk reader for a non-readonly chunk.");
		}
	}

	// creates a bulk reader over a memstream as long as we are cached
	private bool TryCreateBulkMemReader(bool raw, out TFChunkBulkReader reader) {
		lock (_cachedDataLock) {
			if (_cacheStatus != CacheStatus.Cached) {
				reader = null;
				return false;
			}

			if (_cachedData == IntPtr.Zero)
				throw new Exception("Unexpected error: a cached chunk had no cached data");

			Interlocked.Increment(ref _memStreamCount);
			var stream = new UnmanagedMemoryStream((byte*)_cachedData, _cachedLength);

			if (raw) {
				reader = new TFChunkBulkRawReader(chunk: this, streamToUse: stream, isMemory: true);
				return true;
			}

			var streamToUse = new ChunkDataReadStream(stream);
			streamToUse = (_cachedDataTransformed
				? _transform.Read
				: IdentityChunkReadTransform.Instance).TransformData(streamToUse);

			reader = new TFChunkBulkDataReader(chunk: this, streamToUse: streamToUse, isMemory: true);

			return true;
		}
	}

	public void ReleaseReader(TFChunkBulkReader reader) {
		if (reader.IsMemory) {
			var memStreamCount = Interlocked.Decrement(ref _memStreamCount);
			if (memStreamCount < 0)
				throw new Exception("Count of mem streams reduced below zero.");
			if (memStreamCount == 0)
				TryDestructMemStreams();
			return;
		}

		var fileStreamCount = Interlocked.Decrement(ref _fileStreamCount);
		if (fileStreamCount < 0)
			throw new Exception("Count of file streams reduced below zero.");
		if (_selfdestructin54321 && fileStreamCount == 0)
			CleanUpFileStreamDestruction();
	}

	public override string ToString() {
		return string.Format("#{0}-{1} ({2})", _chunkHeader.ChunkStartNumber, _chunkHeader.ChunkEndNumber,
			Path.GetFileName(_filename));
	}

	private struct Midpoint {
		public readonly int ItemIndex;
		public readonly long LogPos;

		public Midpoint(int itemIndex, PosMap posmap) {
			ItemIndex = itemIndex;
			LogPos = posmap.LogPos;
		}

		public override string ToString() {
			return string.Format("ItemIndex: {0}, LogPos: {1}", ItemIndex, LogPos);
		}
	}

	[StructLayout(LayoutKind.Auto)]
	private struct ReaderWorkItemPool() {
		private volatile ReaderWorkItem[] _array;

		// IndexPool supports up to 64 elements with O(1) take/return time complexity.
		// It's a thread-safe data structure with no allocations that provide predictability about
		// the indices: smallest available index is always preferred.
		private IndexPool _indices = new() { IsEmpty = true };

		public void Reuse() {
			if (_array is null) {
				Interlocked.CompareExchange(ref _array, new ReaderWorkItem[IndexPool.Capacity], null);
			}

			_indices.Reset();
		}

		// Skip index and type variance checks which is inserted by runtime typically because
		// the array element is of reference type.
		private static ref ReaderWorkItem UnsafeGetElement(ReaderWorkItem[] array, int index) {
			Debug.Assert((uint)index < (uint)array.Length);

			return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), index);
		}

		// releases all available slots in the pool
		internal int Drain(ref int referenceCount) {
			int localReferenceCount = Interlocked.CompareExchange(ref referenceCount, 0, 0);

			if (_array is { } array && localReferenceCount > 0) {
				Span<int> indices = stackalloc int[IndexPool.Capacity];
				int count = _indices.Take(indices);

				foreach (var index in indices.Slice(0, count)) {
					ref ReaderWorkItem slot = ref UnsafeGetElement(array, index);
					slot?.Dispose();
					slot = null;

					localReferenceCount = Interlocked.Decrement(ref referenceCount);
				}
			}

			return localReferenceCount;
		}

		internal bool TryTake(out Slot slot) {
			if (_array is { } array && _indices.TryTake(out int index)) {
				slot = new(array, index);
				return true;
			}

			slot = default;
			return false;
		}

		internal bool Return(ReaderWorkItem item) {
			int index = item.PositionInPool;
			if (index < 0)
				return false;

			_indices.Return(index);
			return true;
		}

		[StructLayout(LayoutKind.Auto)]
		internal readonly ref struct Slot(ReaderWorkItem[] array, int index) {
			public int Index => index;

			public ref ReaderWorkItem ValueRef => ref UnsafeGetElement(array, index);
		}
	}
}
