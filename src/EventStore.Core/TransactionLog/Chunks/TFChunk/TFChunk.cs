// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers;
using System.Buffers.Binary;
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
using EventStore.Core.Data;
using EventStore.Core.Exceptions;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.TransactionLog.Scavenging.DbAccess;
using EventStore.Core.Transforms;
using EventStore.Core.Transforms.Identity;
using EventStore.Core.Util;
using EventStore.Plugins.Transforms;
using static System.Threading.Timeout;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.TransactionLog.Chunks.TFChunk;

// Exception handling strategy on write: we generally do not attempt to recover from exceptions on write,
// and instead leave the objects in an invalid state. The intention is that these should only be thrown
// in catastrophic scenarios such as disk failure. The originator of the call takes suitable action:
// typically shutdown the server (e.g. StorageWriter) or abandoning the chunk (e.g. Scavenge).
//
// If a CancellationToken we are passed is cancelled, we follow the normal practice and are allowed to
// throw, but not if it would leave the object in an invalid state. It is up to the caller to not cancel
// the call if doing so would cause the caller to shutdown the server.
public partial class TFChunk : IChunkBlob {
	public enum ChunkVersions : byte {
		OriginalNotUsed = 1,
		Unaligned = 2,
		Aligned = 3,
		Transformed = 4,
	}

	public const byte CurrentChunkVersion = (byte) ChunkVersions.Transformed;
	private const int AlignmentSize = 4096;

	private static readonly ILogger Log = Serilog.Log.ForContext<TFChunk>();

	public bool IsReadOnly {
		get { return Interlocked.CompareExchange(ref _isReadOnly, 0, 0) == 1; }
		private set { Interlocked.Exchange(ref _isReadOnly, value ? 1 : 0); }
	}

	public bool IsCached {
		get { return _cacheStatus is CacheStatus.Cached; }
	}

	public bool IsUncached {
		get { return _cacheStatus is CacheStatus.Uncached; }
	}

	public bool Initialized => _lazyInitArgs is null;

	public bool IsRemote { get; }

	// the logical size of (untransformed) data (could be > PhysicalDataSize if scavenged chunk)
	public long LogicalDataSize {
		get { return Interlocked.Read(ref _logicalDataSize); }
	}

	// the physical size of (untransformed) data
	public int PhysicalDataSize {
		get { return _physicalDataSize; }
	}

	// This can be used to locate the chunk regardless of whether it is local or remote.
	// The FileSystem abstraction can understand it, and so can the user.
	public string ChunkLocator => _filename;

	// This can only be used when we know that the chunk is local.
	// Generally ideally only the FileSystem abstraction should know whether the chunk is local or remote,
	// so we want to minimise calls this this as we go, calling the FileSystem instead.
	public string LocalFileName => _filename;

	public int FileSize {
		get { return _fileSize; }
	}

	public ChunkHeader ChunkHeader {
		get {
			ThrowIfNotInitialized();
			return _chunkHeader;
		}
	}

	public ChunkFooter ChunkFooter {
		get {
			ThrowIfNotInitialized();
			return _chunkFooter;
		}
	}

	public ChunkInfo ChunkInfo {
		get {
			if (_lazyInitArgs is (ITransactionFileTracker, int startNumber))
				return new() {
					// only remote chunks are lazily initialized
					// and all remote chunks have a range of 1.
					ChunkStartNumber = startNumber,
					ChunkEndNumber = startNumber,
					IsRemote = true,
				};

			if (_chunkHeader is not null)
				return new() {
					ChunkStartNumber = _chunkHeader.ChunkStartNumber,
					ChunkEndNumber = _chunkHeader.ChunkEndNumber,
					IsRemote = IsRemote,
				};

			throw CreateNotInitializedException();
		}
	}

	public ReadOnlyMemory<byte> TransformHeader {
		get {
			ThrowIfNotInitialized();
			return _transformHeader;
		}
	}

	private readonly int _midpointsDepth;

	public int RawWriterPosition {
		get {
			return (int)(_writerWorkItem?.WorkingStream.Position
				?? throw new InvalidOperationException(string.Format("TFChunk {0} is not in write mode.", ChunkLocator)));
		}
	}

	private readonly bool _inMem;
	private readonly string _filename;
	private IChunkHandle _handle;
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
	private readonly AsyncExclusiveLock _cachedDataLock = new();
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

	private readonly ManualResetEventSlim _destroyEvent = new(false);
	private volatile bool _selfdestructin54321;
	private volatile bool _deleteFile;
	private readonly bool _unbuffered;
	private readonly bool _writeThrough;
	private volatile ITuple _lazyInitArgs; // null if initialized

	// https://learn.microsoft.com/en-US/troubleshoot/windows-server/application-management/operating-system-performance-degrades
	private readonly bool _reduceFileCachePressure;
	private readonly IChunkFileSystem _fileSystem;

	private IChunkReadSide _readSide;

	private readonly IGetChunkTransformFactory _getTransformFactory;
	private IChunkTransform _transform;
	private ReadOnlyMemory<byte> _transformHeader;

	private TFChunk(string filename,
		int midpointsDepth,
		bool inMem,
		bool unbuffered,
		bool writethrough,
		bool reduceFileCachePressure,
		IChunkFileSystem fileSystem,
		IGetChunkTransformFactory getTransformFactory) {
		Ensure.NotNullOrEmpty(filename, "filename");
		Ensure.Nonnegative(midpointsDepth, "midpointsDepth");

		_filename = filename;
		_midpointsDepth = midpointsDepth;
		_inMem = inMem;
		_unbuffered = unbuffered;
		_writeThrough = writethrough;
		_reduceFileCachePressure = reduceFileCachePressure;
		_memStreams = new();
		_fileStreams = new();
		_fileSystem = fileSystem;
		_getTransformFactory = getTransformFactory;

		IsRemote = !_inMem && _fileSystem.IsRemote(ChunkLocator);

		// Workaround: the lock is used by the finalizer. When the finalizer is called by .NET,
		// the lock is already finalized (and Dispose is called) and cannot be used. To avoid that situation,
		// we suppress the finalizer for the lock. Anyway, the lock doesn't hold any unmanaged resources.
		GC.SuppressFinalize(_cachedDataLock);
	}

	~TFChunk() {
		FreeCachedData();
	}

	private static InvalidOperationException CreateNotInitializedException([CallerMemberName] string memberName = "")
		=> new($"The chunk is not yet initialized to access the member {memberName}. Call EnsureInitialized() first.");

	private void ThrowIfNotInitialized([CallerMemberName] string memberName = "") {
		if (_lazyInitArgs is not null)
			throw CreateNotInitializedException(memberName);
	}

	private static TFChunk FromCompletedFile(IChunkFileSystem fileSystem, string filename, bool unbufferedRead,
		IGetChunkTransformFactory getTransformFactory, bool reduceFileCachePressure)
		=> new(filename, TFConsts.MidpointsDepth, false, unbufferedRead, false, reduceFileCachePressure, fileSystem,
			getTransformFactory);

	// local or remote
	public static async ValueTask<TFChunk> FromCompletedFile(IChunkFileSystem fileSystem, string filename,
		bool verifyHash, bool unbufferedRead,
		ITransactionFileTracker tracker, IGetChunkTransformFactory getTransformFactory,
		bool reduceFileCachePressure, ChunkHeader header = null, ChunkFooter footer = null,
		CancellationToken token = default) {

		var chunk = FromCompletedFile(fileSystem, filename, unbufferedRead, getTransformFactory,
			reduceFileCachePressure);

		try {
			await chunk.InitCompleted(verifyHash, tracker, header, footer, token);
		} catch {
			chunk.Dispose();
			throw;
		}

		return chunk;
	}

	// local or remote
	public static async ValueTask<TFChunk> FromCompletedFile(IChunkFileSystem fileSystem, string filename,
		bool verifyHash, bool unbufferedRead,
		ITransactionFileTracker tracker, IGetChunkTransformFactory getTransformFactory,
		bool reduceFileCachePressure, int startNumber, CancellationToken token = default) {

		var chunk = FromCompletedFile(fileSystem, filename, unbufferedRead, getTransformFactory,
			reduceFileCachePressure);

		if (chunk.IsRemote) {
			chunk.IsReadOnly = true;
			chunk._lazyInitArgs = (tracker, startNumber);
		} else {
			try {
				await chunk.InitCompleted(verifyHash, tracker, header: null, footer: null, token);
			} catch {
				chunk.Dispose();
				throw;
			}
		}

		return chunk;
	}

	// always local
	public static async ValueTask<TFChunk> FromOngoingFile(IChunkFileSystem fileSystem, string filename, int writePosition, bool unbuffered,
		bool writethrough, bool reduceFileCachePressure, ITransactionFileTracker tracker,
		IGetChunkTransformFactory getTransformFactory,
		CancellationToken token) {
		var chunk = new TFChunk(filename,
			TFConsts.MidpointsDepth,
			false,
			unbuffered,
			writethrough,
			reduceFileCachePressure,
			fileSystem: fileSystem,
			getTransformFactory: getTransformFactory);
		try {
			await chunk.InitOngoing(writePosition, tracker, getTransformFactory, token);
		} catch {
			chunk.Dispose();
			throw;
		}

		return chunk;
	}

	// always local
	public static async ValueTask<TFChunk> CreateNew(
		IChunkFileSystem fileSystem,
		string filename,
		int chunkDataSize,
		int chunkStartNumber,
		int chunkEndNumber,
		bool isScavenged,
		bool inMem,
		bool unbuffered,
		bool writethrough,
		bool reduceFileCachePressure,
		ITransactionFileTracker tracker,
		IGetChunkTransformFactory getTransformFactory,
		CancellationToken token) {
		var transformFactory = getTransformFactory.ForNewChunk();
		var version = CurrentChunkVersion;
		var minCompatibleVersion = transformFactory.Type == TransformType.Identity
			? (byte) ChunkVersions.Aligned
			: version;

		var chunkHeader = new ChunkHeader(version, minCompatibleVersion, chunkDataSize, chunkStartNumber, chunkEndNumber,
			isScavenged, Guid.NewGuid(), transformFactory.Type);
		var fileSize = GetAlignedSize(transformFactory.TransformDataPosition(chunkDataSize) + ChunkHeader.Size + ChunkFooter.Size);

		var transformHeader = transformFactory.TransformHeaderLength > 0
			? new byte[transformFactory.TransformHeaderLength]
			: [];

		transformFactory.CreateTransformHeader(transformHeader);

		return await CreateWithHeader(fileSystem, filename, chunkHeader, fileSize, inMem, unbuffered, writethrough,
			reduceFileCachePressure, tracker, transformFactory, getTransformFactory, transformHeader, token);
	}

	// always local
	public static async ValueTask<TFChunk> CreateWithHeader(
		IChunkFileSystem fileSystem,
		string filename,
		ChunkHeader header,
		int fileSize,
		bool inMem,
		bool unbuffered,
		bool writethrough,
		bool reduceFileCachePressure,
		ITransactionFileTracker tracker,
		IChunkTransformFactory transformFactory,
		IGetChunkTransformFactory getTransformFactory,
		ReadOnlyMemory<byte> transformHeader,
		CancellationToken token) {
		var chunk = new TFChunk(filename,
			TFConsts.MidpointsDepth,
			inMem,
			unbuffered,
			writethrough,
			reduceFileCachePressure,
			fileSystem: fileSystem,
			getTransformFactory: getTransformFactory);
		try {
			await chunk.InitNew(header, fileSize, tracker, transformFactory, transformHeader, token);
		} catch {
			chunk.Dispose();
			throw;
		}

		return chunk;
	}

	public ValueTask EnsureInitialized(CancellationToken token) => _lazyInitArgs switch {
		(ITransactionFileTracker tracker, int) => EnsureInitAsCompletedCore(tracker, token),
		_ => ValueTask.CompletedTask,
	};

	private async ValueTask EnsureInitAsCompletedCore(ITransactionFileTracker tracker,
		CancellationToken token) {
		// lazy init is based on the double check pattern implemented on top of existing lock
		await _cachedDataLock.AcquireAsync(token);
		try {
			if (_lazyInitArgs is not null) {
				await InitCompleted(verifyHash: false, tracker, header: null, footer: null, token);

				// cannot be reordered, so we know that this flag is set to 'true' when all fields initialized properly
				_lazyInitArgs = null;
			}
		} catch {
			// revert internal state
			if (_handle is not null) {
				_handle.Dispose();
				_handle = null;
			}

			_transform = null;
			_transformHeader = ReadOnlyMemory<byte>.Empty;
			_chunkFooter = null;
			_chunkHeader = null;

			throw;
		} finally {
			_cachedDataLock.Release();
		}
	}

	private async ValueTask InitCompleted(bool verifyHash, ITransactionFileTracker tracker, ChunkHeader header, ChunkFooter footer, CancellationToken token) {
		_handle = await _fileSystem.OpenForReadAsync(
			ChunkLocator,
			_reduceFileCachePressure
				? IChunkFileSystem.ReadOptimizationHint.None
				: IChunkFileSystem.ReadOptimizationHint.RandomAccess,
			token);
		await _fileSystem.SetReadOnlyAsync(ChunkLocator, true, token);
		_fileSize = (int)_handle.Length;

		IsReadOnly = true;

		await using (var stream = _handle.CreateStream()) {
			if (header is null) {
				_chunkHeader = await ReadHeader(stream, token);
			} else {
				_chunkHeader = header;
				stream.Position = ChunkHeader.Size;
			}
			Log.Debug("Opened completed {chunk} as version {version} (min. compatible version: {minCompatibleVersion})", ChunkLocator, _chunkHeader.Version, _chunkHeader.MinCompatibleVersion);

			if (_chunkHeader.MinCompatibleVersion > CurrentChunkVersion)
				throw new CorruptDatabaseException(new UnsupportedFileVersionException(ChunkLocator, _chunkHeader.MinCompatibleVersion,
					CurrentChunkVersion));

			var transformFactory = _getTransformFactory.ForExistingChunk(_chunkHeader.TransformType);

			var transformHeader = transformFactory.TransformHeaderLength > 0
				? new byte[transformFactory.TransformHeaderLength]
				: [];
			await transformFactory.ReadTransformHeader(stream, transformHeader, token);
			_transformHeader = transformHeader;
			_transform = transformFactory.CreateTransform(transformHeader);

			_chunkFooter = footer ?? await ReadFooter(stream, token);
			if (!_chunkFooter.IsCompleted) {
				throw new CorruptDatabaseException(new BadChunkInDatabaseException(
					$"Chunk file '{ChunkLocator}' should be completed, but is not."));
			}

			_logicalDataSize = _chunkFooter.LogicalDataSize;
			_physicalDataSize = _chunkFooter.PhysicalDataSize;
		}

		CreateReaderStreams();

		_readSide = _chunkHeader.IsScavenged
			? new TFChunkReadSideScavenged(this, tracker)
			: new TFChunkReadSideUnscavenged(this, tracker);

		if (verifyHash)
			await VerifyFileHash(token);
	}

	private async ValueTask InitNew(ChunkHeader chunkHeader, int fileSize, ITransactionFileTracker tracker,
		IChunkTransformFactory transformFactory,
		ReadOnlyMemory<byte> transformHeader,
		CancellationToken token) {
		Ensure.NotNull(chunkHeader, "chunkHeader");
		Ensure.Positive(fileSize, "fileSize");

		_fileSize = fileSize;
		IsReadOnly = false;
		_chunkHeader = chunkHeader;
		_physicalDataSize = 0;
		_logicalDataSize = 0;

		_transformHeader = transformHeader;
		_transform = transformFactory.CreateTransform(transformHeader.Span);

		if (_inMem)
			await CreateInMemChunk(chunkHeader, fileSize, transformHeader, token);
		else {
			await CreateWriterWorkItemForNewChunk(chunkHeader, fileSize, transformHeader, token);
		}

		_readSide = chunkHeader.IsScavenged
			? new TFChunkReadSideScavenged(this, tracker)
			: new TFChunkReadSideUnscavenged(this, tracker);

		// Always cache the active chunk
		// If the chunk is scavenged we will definitely mark it readonly before we are done writing to it.
		if (!chunkHeader.IsScavenged) {
			await CacheInMemory(token);
		}
	}

	private async ValueTask InitOngoing(int writePosition, ITransactionFileTracker tracker,
		IGetChunkTransformFactory getTransformFactory,
		CancellationToken token) {
		Ensure.Nonnegative(writePosition, "writePosition");
		var fileInfo = new FileInfo(LocalFileName);
		if (!fileInfo.Exists)
			throw new CorruptDatabaseException(new ChunkNotFoundException(ChunkLocator));

		_fileSize = (int)fileInfo.Length;
		IsReadOnly = false;
		_physicalDataSize = writePosition;
		_logicalDataSize = writePosition;

		await _fileSystem.SetReadOnlyAsync(fileInfo.FullName, false, token);
		_chunkHeader = await CreateWriterWorkItemForExistingChunk(writePosition, getTransformFactory, token);
		Log.Debug("Opened ongoing {chunk} as version {version} (min. compatible version: {minCompatibleVersion})", ChunkLocator, _chunkHeader.Version, _chunkHeader.MinCompatibleVersion);

		if (_chunkHeader.MinCompatibleVersion > CurrentChunkVersion)
			throw new CorruptDatabaseException(new UnsupportedFileVersionException(ChunkLocator, _chunkHeader.MinCompatibleVersion,
				CurrentChunkVersion));

		_readSide = new TFChunkReadSideUnscavenged(this, tracker);

		// Always cache the active chunk
		await CacheInMemory(token);
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

	private async ValueTask CreateInMemChunk(ChunkHeader chunkHeader, int fileSize, ReadOnlyMemory<byte> transformHeader, CancellationToken token) {
		var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

		// ALLOCATE MEM
		_cacheStatus = CacheStatus.Cached;
		_cachedLength = fileSize;
		_cachedData = Marshal.AllocHGlobal(_cachedLength);
		_cachedDataTransformed = true;
		GC.AddMemoryPressure(_cachedLength);


		// WRITE HEADERS
		await using (var headerStream = CreateMemoryStream(_cachedLength, FileAccess.ReadWrite)) {
			await WriteHeader(md5, headerStream, chunkHeader, token);
			await WriteTransformHeader(md5, headerStream, transformHeader, token);
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

	private unsafe Stream CreateSharedMemoryStream() {
		Debug.Assert(_cachedData is not 0);
		Debug.Assert(_cachedLength > 0);

		ReadOnlyMemory<byte> memoryView = UnmanagedMemory.AsMemory((byte*)_cachedData, _cachedLength);
		return StreamSource.AsSharedStream(new(memoryView), compatWithAsync: true);
	}

	private FileOptions WritableHandleOptions {
		get {
			var options = _reduceFileCachePressure
				? ChunkFileHandle.DefaultFileOptions
				: ChunkFileHandle.DefaultFileOptions | FileOptions.RandomAccess;
			if (_writeThrough)
				options |= FileOptions.WriteThrough;

			return options;
		}
	}

	private async ValueTask CreateWriterWorkItemForNewChunk(ChunkHeader chunkHeader, int fileSize, ReadOnlyMemory<byte> transformHeader, CancellationToken token) {
		var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

		// create temp file first and set desired length
		// if there is not enough disk space or something else prevents file to be resized as desired
		// we'll end up with empty temp file, which won't trigger false error on next DB verification
		var tempFilename = $"{LocalFileName}.{Guid.NewGuid()}.tmp";
		var options = new FileStreamOptions {
			Mode = FileMode.CreateNew,
			Access = FileAccess.ReadWrite,
			Share = FileShare.Read,
			Options = FileOptions.SequentialScan | ChunkFileHandle.DefaultFileOptions,
			PreallocationSize = fileSize, // avoid fragmentation of file
			BufferSize = WriterWorkItem.BufferSize,
		};

		await using (var tempFile = new FileStream(tempFilename, options)) {
			tempFile.SetLength(fileSize);

			// we need to write header into temp file before moving it into correct chunk place, so in case of crash
			// we don't end up with seemingly valid chunk file with no header at all...
			await WriteHeader(md5, tempFile, chunkHeader, token);
			await WriteTransformHeader(md5, tempFile, transformHeader, token);

			tempFile.FlushToDisk();
		}

		File.Move(tempFilename, LocalFileName);

		// reuse FileStreamOptions instance
		options.Mode = FileMode.Open;
		options.Options = WritableHandleOptions;
		options.PreallocationSize = 0L;
		_handle = ChunkFileHandle.Create(LocalFileName, options);
		_writerWorkItem = new(_handle, md5, _unbuffered, _transform.Write, ChunkHeader.Size + transformHeader.Length);
		await _fileSystem.SetReadOnlyAsync(LocalFileName, false, token);
	}

	private async ValueTask<ChunkHeader> CreateWriterWorkItemForExistingChunk(int writePosition,
		IGetChunkTransformFactory getTransformFactory,
		CancellationToken token) {
		var options = new FileStreamOptions {
			Mode = FileMode.Open,
			Access = FileAccess.ReadWrite,
			Share = FileShare.Read,
			Options = WritableHandleOptions,
		};

		_handle = ChunkFileHandle.Create(LocalFileName, options);

		var stream = _handle.CreateStream();
		ChunkHeader chunkHeader;
		try {
			chunkHeader = await ReadHeader(stream, token);
			if (chunkHeader.Version is (byte)ChunkVersions.Unaligned) {
				Log.Debug("Upgrading ongoing file {chunk} to version 3", ChunkLocator);
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

				using (var buffer = Memory.AllocateExactly<byte>(ChunkHeader.Size)) {
					await stream.WriteAsync(newHeader, buffer.Memory, token);
				}

				await stream.FlushAsync(token);
			}

			var transformFactory = getTransformFactory.ForExistingChunk(chunkHeader.TransformType);

			var transformHeader = transformFactory.TransformHeaderLength > 0
				? new byte[transformFactory.TransformHeaderLength]
				: [];
			await transformFactory.ReadTransformHeader(stream, transformHeader, token);
			_transformHeader = transformHeader;
			_transform = transformFactory.CreateTransform(transformHeader);
		} catch {
			_handle.Dispose();
			throw;
		} finally {
			await stream.DisposeAsync();
		}

		var workItem = new WriterWorkItem(_handle, IncrementalHash.CreateHash(HashAlgorithmName.MD5), _unbuffered, _transform.Write, 0);
		var realPosition = GetRawPosition(writePosition);
		// the writer work item's stream is responsible for computing the current checksum when the position is set
		workItem.WorkingStream.Position = realPosition;
		_writerWorkItem = workItem;

		return chunkHeader;
	}

	private static async ValueTask WriteHeader(IncrementalHash md5, Stream stream, ChunkHeader chunkHeader, CancellationToken token) {
		var chunkHeaderBytes = ArrayPool<byte>.Shared.Rent(ChunkHeader.Size);
		try {
			chunkHeader.Format(chunkHeaderBytes);
			md5.AppendData(chunkHeaderBytes, 0, ChunkHeader.Size);
			await stream.WriteAsync(chunkHeaderBytes.AsMemory(0, ChunkHeader.Size), token);
		} finally {
			ArrayPool<byte>.Shared.Return(chunkHeaderBytes);
		}
	}

	private static ValueTask WriteTransformHeader(IncrementalHash md5, Stream stream, ReadOnlyMemory<byte> transformHeader, CancellationToken token) {
		if (transformHeader.IsEmpty)
			return ValueTask.CompletedTask;

		md5.AppendData(transformHeader.Span);
		return stream.WriteAsync(transformHeader, token);
	}

	public async ValueTask VerifyFileHash(CancellationToken token) {
		if (!IsReadOnly)
			throw new InvalidOperationException("You can't verify hash of not-completed TFChunk.");

		if (IsRemote) {
			Log.Debug("Skipped verifying hash for TFChunk '{chunk}' because it is remote", ChunkLocator);
			return;
		}

		Log.Debug("Verifying hash for TFChunk '{chunk}'...", ChunkLocator);
		using var reader = await AcquireRawReader(token);
		reader.Stream.Seek(0, SeekOrigin.Begin);
		var stream = reader.Stream;
		var footer = _chunkFooter;

		using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

		// hash whole chunk except MD5 hash sum which should always be last
		await MD5Hash.ContinuousHashFor(md5, stream, 0, _fileSize - ChunkFooter.ChecksumSize, token);
		VerifyHash(footer.MD5Hash, md5);

		static void VerifyHash(ReadOnlySpan<byte> expected, IncrementalHash actual) {
			Span<byte> buffer = stackalloc byte[ChunkFooter.ChecksumSize];
			var bytesWritten = actual.GetCurrentHash(buffer);
			Debug.Assert(bytesWritten is ChunkFooter.ChecksumSize);

			// Perf: use hardware accelerated byte array comparison
			if (!expected.SequenceEqual(buffer.Slice(0, bytesWritten)))
				throw new HashValidationException();
		}
	}

	private ValueTask<ChunkHeader> ReadHeader(Stream stream, CancellationToken token) {
		if (stream.Length < ChunkHeader.Size) {
			return ValueTask.FromException<ChunkHeader>(new CorruptDatabaseException(new BadChunkInDatabaseException(
				$"Chunk file '{ChunkLocator}' is too short to even read ChunkHeader, its size is {stream.Length} bytes.")));
		}

		stream.Seek(0, SeekOrigin.Begin);
		return ChunkHeader.FromStream(stream, token);
	}

	private ValueTask<ChunkFooter> ReadFooter(Stream stream, CancellationToken token) {
		if (stream.Length < ChunkFooter.Size) {
			return ValueTask.FromException<ChunkFooter>(new CorruptDatabaseException(new BadChunkInDatabaseException(
				$"Chunk file '{ChunkLocator}' is too short to even read ChunkFooter, its size is {stream.Length} bytes.")));
		}

		stream.Seek(-ChunkFooter.Size, SeekOrigin.End);
		return ChunkFooter.FromStream(stream, token);
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
	public async ValueTask<long> GetActualRawPosition(long logicalPosition, CancellationToken token) {
		ArgumentOutOfRangeException.ThrowIfNegative(logicalPosition);
		ThrowIfNotInitialized();

		var actualPosition = await _readSide.GetActualPosition(logicalPosition, token);

		return actualPosition < 0 ? -1 : GetRawPosition(actualPosition);
	}

	public async ValueTask CacheInMemory(CancellationToken token) {
		if (_inMem)
			return;

		ThrowIfNotInitialized();
		await _cachedDataLock.AcquireAsync(token);
		try {
			if (_cacheStatus is not CacheStatus.Uncached) {
				// expected to be very rare
				if (_cacheStatus is CacheStatus.Uncaching)
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
					await BuildCacheArray(
						size: GetAlignedSize(ChunkHeader.Size + _chunkHeader.ChunkSize + ChunkFooter.Size),
						reader: await AcquireFileReader(raw: false, token),
						offset: ChunkHeader.Size,
						count: _physicalDataSize,
						transformed: false,
						token);
				else
					await BuildCacheArray(
						size: _fileSize,
						reader: await AcquireFileReader(raw: true, token),
						offset: 0,
						count: _fileSize,
						transformed: true,
						token);
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
				if (Interlocked.Add(ref _memStreamCount, -IndexPool.Capacity) is 0)
					FreeCachedDataUnsafe();
				Log.Debug("CACHING ABORTED for TFChunk {chunk} as TFChunk was probably marked for deletion.", this);
				return;
			}

			if (_writerWorkItem is { } writerWorkItem) {
				UnmanagedMemoryStream memStream;
				unsafe {
					memStream =
						new((byte*)_cachedData, _cachedLength, _cachedLength, FileAccess.ReadWrite) {
							Position = writerWorkItem.WorkingStream.Position,
						};
				}

				writerWorkItem.SetMemStream(memStream);
			}

			Log.Debug("CACHED TFChunk {chunk} in {elapsed}.", this, sw.Elapsed);

			if (_selfdestructin54321)
				TryDestructMemStreamsUnsafe();

			_cacheStatus = CacheStatus.Cached;
		} finally {
			_cachedDataLock.Release();
		}
	}

	private async ValueTask BuildCacheArray(int size, TFChunkBulkReader reader, int offset, int count, bool transformed, CancellationToken token) {
		Debug.Assert(_cachedDataLock.IsLockHeld);

		try {
			if (reader.IsMemory)
				throw new InvalidOperationException(
					"When trying to build cache, reader worker is already in-memory reader.");

			_cachedLength = size;
			var cachedData = Marshal.AllocHGlobal(_cachedLength);
			GC.AddMemoryPressure(_cachedLength);

			try {
				Memory<byte> memoryView;
				unsafe {
					memoryView = UnmanagedMemory.AsMemory((byte*)(cachedData + offset), count);
				}

				reader.Stream.Seek(offset, SeekOrigin.Begin);
				await reader.Stream.ReadExactlyAsync(memoryView, token);
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

	public async ValueTask UnCacheFromMemory(CancellationToken token) {
		await _cachedDataLock.AcquireAsync(token);
		try {
			if (_inMem)
				return;
			if (_cacheStatus is CacheStatus.Cached) {
				// we won the right to un-cache and chunk was cached
				_writerWorkItem?.DisposeMemStream();

				Log.Debug("UNCACHING TFChunk {chunk}.", this);
				_cacheStatus = CacheStatus.Uncaching;
				// this memory barrier corresponds to the barrier in ReturnReaderWorkItem
				Thread.MemoryBarrier();
				TryDestructMemStreamsUnsafe();
			}
		} finally {
			_cachedDataLock.Release();
		}
	}

	public ValueTask<bool> ExistsAt(long logicalPosition, CancellationToken token)
		=> Initialized
			? _readSide.ExistsAt(logicalPosition, token)
			: ValueTask.FromException<bool>(CreateNotInitializedException());

	public ValueTask<RecordReadResult> TryReadAt(long logicalPosition, bool couldBeScavenged, CancellationToken token)
		=> Initialized
			? _readSide.TryReadAt(logicalPosition, couldBeScavenged, token)
			: ValueTask.FromException<RecordReadResult>(CreateNotInitializedException());

	public ValueTask<RecordReadResult> TryReadFirst(CancellationToken token)
		=> Initialized
			? _readSide.TryReadFirst(token)
			: ValueTask.FromException<RecordReadResult>(CreateNotInitializedException());

	public ValueTask<RecordReadResult> TryReadClosestForward(long logicalPosition, CancellationToken token)
		=> Initialized
			? _readSide.TryReadClosestForward(logicalPosition, token)
			: ValueTask.FromException<RecordReadResult>(CreateNotInitializedException());

	public ValueTask<RawReadResult> TryReadClosestForwardRaw(long logicalPosition, Func<int, byte[]> getBuffer,
		CancellationToken token)
		=> Initialized
			? _readSide.TryReadClosestForwardRaw(logicalPosition, getBuffer, token)
			: ValueTask.FromException<RawReadResult>(CreateNotInitializedException());

	public ValueTask<RecordReadResult> TryReadLast(CancellationToken token)
		=> Initialized
			? _readSide.TryReadLast(token)
			: ValueTask.FromException<RecordReadResult>(CreateNotInitializedException());

	public ValueTask<RecordReadResult> TryReadClosestBackward(long logicalPosition, CancellationToken token)
		=> Initialized
			? _readSide.TryReadClosestBackward(logicalPosition, token)
			: ValueTask.FromException<RecordReadResult>(CreateNotInitializedException());

	public async ValueTask<RecordWriteResult> TryAppend(ILogRecord record, CancellationToken token) {
		if (IsReadOnly)
			throw new InvalidOperationException("Cannot write to a read-only block.");

		ThrowIfNotInitialized();
		var workItem = _writerWorkItem;
		long oldPosition;
		int length;
		using (var dataOnDisk = SerializeLogRecord(record, out length)) {
			oldPosition = GetDataPosition(workItem);
			if (workItem.WorkingStream.Position + length + 2 * sizeof(int) > ChunkHeader.Size + _chunkHeader.ChunkSize)
				return RecordWriteResult.Failed(oldPosition);

			await workItem.AppendData(dataOnDisk.Memory, token);
		}

		_physicalDataSize = (int)GetDataPosition(workItem); // should fit 32 bits
		_logicalDataSize = ChunkHeader.GetLocalLogPosition(record.LogPosition + length + 2 * sizeof(int));

		// for non-scavenged chunk _physicalDataSize should be the same as _logicalDataSize
		// for scavenged chunk _logicalDataSize should be at least the same as _physicalDataSize
		if ((!ChunkHeader.IsScavenged && _logicalDataSize != _physicalDataSize)
		    || (ChunkHeader.IsScavenged && _logicalDataSize < _physicalDataSize)) {
			throw new Exception(
				$"Data sizes violation. Chunk: {ChunkLocator}, IsScavenged: {ChunkHeader.IsScavenged}, LogicalDataSize: {_logicalDataSize}, PhysicalDataSize: {_physicalDataSize}.");
		}

		return RecordWriteResult.Successful(oldPosition, _physicalDataSize);

		static MemoryOwner<byte> SerializeLogRecord(ILogRecord record, out int recordLength) {
			var writer = new BufferWriterSlim<byte>(record.GetSizeWithLengthPrefixAndSuffix());
			writer.Advance(sizeof(int)); // reserved for length prefix
			record.WriteTo(ref writer);

			recordLength = writer.WrittenCount - sizeof(int);
			writer.WriteLittleEndian(recordLength); // length suffix

			var buffer = writer.DetachOrCopyBuffer();
			Debug.Assert(record.GetSizeWithLengthPrefixAndSuffix() == buffer.Length);

			// write length prefix
			BinaryPrimitives.WriteInt32LittleEndian(buffer.Span, recordLength);
			return buffer;
		}
	}

	public async ValueTask<bool> TryAppendRawData(ReadOnlyMemory<byte> buffer, CancellationToken token) {
		ThrowIfNotInitialized();
		var workItem = _writerWorkItem;
		if (workItem.WorkingStream.Position + buffer.Length > workItem.WorkingStream.Length)
			return false;
		await workItem.AppendData(buffer, token);
		return true;
	}

	public ValueTask Flush(CancellationToken token)
		=> IsReadOnly ? ValueTask.CompletedTask : _writerWorkItem.FlushToDisk(token);

	public ValueTask Complete(CancellationToken token) {
		if (!Initialized)
			return ValueTask.FromException(CreateNotInitializedException());

		return ChunkHeader.IsScavenged
			? ValueTask.FromException(
				new InvalidOperationException("CompleteScavenged should be used for scavenged chunks."))
			: CompleteNonRaw(null, token);
	}

	public ValueTask CompleteScavenge(IReadOnlyCollection<PosMap> mapping, CancellationToken token) {
		if (!Initialized)
			return ValueTask.FromException(CreateNotInitializedException());

		return ChunkHeader.IsScavenged
			? CompleteNonRaw(mapping, token)
			: ValueTask.FromException(
				new InvalidOperationException("CompleteScavenged should not be used for non-scavenged chunks."));
	}

	private async ValueTask CompleteNonRaw(IReadOnlyCollection<PosMap> mapping, CancellationToken token) {
		if (IsReadOnly)
			throw new InvalidOperationException("Cannot complete a read-only TFChunk.");

		_chunkFooter = await WriteFooter(mapping, token); // WriteFooter always calls Flush

		// cannot be canceled from here on
		if (!_inMem) {
			await _fileSystem.SetReadOnlyAsync(ChunkLocator, true, CancellationToken.None);
		}

		if (!_inMem) {
			CreateReaderStreams();
		}

		IsReadOnly = true;

		_writerWorkItem?.Dispose();
		_writerWorkItem = null;
	}

	public async ValueTask CompleteRaw(CancellationToken token) {
		if (IsReadOnly)
			throw new InvalidOperationException("Cannot complete a read-only TFChunk.");

		ThrowIfNotInitialized();
		if (_writerWorkItem.WorkingStream.Position != _writerWorkItem.WorkingStream.Length)
			throw new InvalidOperationException("The raw chunk is not completely written.");

		await Flush(token);

		if (!_inMem) {
			await _fileSystem.SetReadOnlyAsync(ChunkLocator, true, token);
			await using var stream = _handle.CreateStream();
			_chunkFooter = await ReadFooter(stream, token);
		} else {
			_chunkFooter = await ReadFooter(_sharedMemStream, token);
		}

		// cannot be canceled from here on
		if (!_inMem)
			CreateReaderStreams();

		IsReadOnly = true;

		_writerWorkItem?.Dispose();
		_writerWorkItem = null;
	}

	private async ValueTask<ChunkFooter> WriteFooter(IReadOnlyCollection<PosMap> mapping, CancellationToken token) {
		token.ThrowIfCancellationRequested();

		// cannot be canceled from here on
		var workItem = _writerWorkItem;
		int mapSize;

		// reuse rented buffer for position mapping serialization and chunk footer
		byte[] bufferFromPool;
		if (mapping is null) {
			mapSize = 0;
			bufferFromPool = ArrayPool<byte>.Shared.Rent(ChunkFooter.Size);
		} else {
			if (_inMem)
				throw new InvalidOperationException(
					"Cannot write an in-memory chunk with a PosMap. " +
					"Scavenge is not supported on in-memory databases");

			if (_cacheStatus is not CacheStatus.Uncached) {
				throw new InvalidOperationException("Trying to write mapping while chunk is cached. "
				                                    + "You probably are writing scavenged chunk as cached. "
				                                    + "Do not do this.");
			}

			mapSize = mapping.Count * PosMap.FullSize;

			bufferFromPool = ArrayPool<byte>.Shared.Rent(Math.Max(mapSize, ChunkFooter.Size));
			mapSize = WriteMapping(bufferFromPool, mapping);
			await workItem.AppendData(bufferFromPool.AsMemory(0, mapSize), CancellationToken.None);
		}

		await _transform.Write.CompleteData(
			footerSize: ChunkFooter.Size,
			alignmentSize: _chunkHeader.Version >= (byte)ChunkVersions.Aligned ? AlignmentSize : 1,
			CancellationToken.None);

		int fileSize;
		ChunkFooter footerWithHash;
		try {
			var footerNoHash = new ChunkFooter(true, true, _physicalDataSize, LogicalDataSize, mapSize);

			//MD5
			footerNoHash.Format(bufferFromPool);
			workItem.MD5.AppendData(bufferFromPool, 0,
				ChunkFooter.Size - ChunkFooter.ChecksumSize);

			//FILE
			footerWithHash = new ChunkFooter(true, true, _physicalDataSize, LogicalDataSize, mapSize, workItem.MD5);
			footerWithHash.Format(bufferFromPool);
			fileSize = await _transform.Write.WriteFooter(new(bufferFromPool, 0, ChunkFooter.Size), CancellationToken.None);
		} finally {
			ArrayPool<byte>.Shared.Return(bufferFromPool);
		}

		// at this point in code, 'WorkingStream` should not contain buffered bytes because SeLength
		// can cause sync-over-async write. This fact is checked within `IChunkHandle.UnbufferedStream` class
		workItem.ResizeFileStream(fileSize);

		_fileSize = fileSize;
		return footerWithHash;

		static int WriteMapping(Span<byte> buffer, IReadOnlyCollection<PosMap> mapping) {
			var writer = new SpanWriter<byte>(buffer);
			foreach (var map in mapping) {
				writer.Write(map);
			}

			return writer.WrittenCount;
		}
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

	// Causes the chunk to be deleted when all the readers have returned.
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
		if (Interlocked.CompareExchange(ref _cleanedUpFileStreams, 1, 0) is not 0)
			return;

		if (_writerWorkItem is not null) {
			_writerWorkItem.FlushToDisk();
			_writerWorkItem.Dispose();
		}

		if (!_inMem) {
			_handle?.Dispose();
			Helper.EatException(LocalFileName, static filename => File.SetAttributes(filename, FileAttributes.Normal));

			if (_deleteFile) {
				Log.Information("Deleting chunk {chunk}",
					Path.GetFileName(ChunkLocator));
				Helper.EatException(LocalFileName, File.Delete);
			}
		}

		_destroyEvent.Set();
	}

	public static int GetAlignedSize(int size) {
		if (size % AlignmentSize == 0) return size;
		return (size / AlignmentSize + 1) * AlignmentSize;
	}

	// the caller must be responsible to obtain the lock to call this method
	private bool TryDestructMemStreamsUnsafe() {
		Debug.Assert(_cachedDataLock.IsLockHeld);

		if (_cacheStatus is not CacheStatus.Uncaching && !_selfdestructin54321)
			return false;

		_writerWorkItem?.DisposeMemStream();

		switch (_memStreams.Drain(ref _memStreamCount)) {
			case < 0:
				throw new Exception("Count of memory streams reduced below zero.");
			case 0:
				// make sure "the light is off" for memory streams
				FreeCachedDataUnsafe();
				return true;
			default:
				return false;
		}
	}

	private bool TryDestructMemStreams() {
		_cachedDataLock.TryAcquire(InfiniteTimeSpan);
		try {
			return TryDestructMemStreamsUnsafe();
		} finally {
			_cachedDataLock.Release();
		}
	}

	// the caller must be responsible to obtain the lock to call this method
	private void FreeCachedDataUnsafe() {
		Debug.Assert(_cachedDataLock.IsLockHeld);

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

	private void FreeCachedData() {
		_cachedDataLock.TryAcquire(InfiniteTimeSpan);
		try {
			FreeCachedDataUnsafe();
		} finally {
			_cachedDataLock.Release();
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
				slot.ValueRef = fileStreamWorkItem = new(_handle, _transform.Read, IsRemote) { PositionInPool = slot.Index };

			return fileStreamWorkItem;
		}

		Interlocked.Increment(ref _fileStreamCount);

		if (_selfdestructin54321) {
			if (Interlocked.Decrement(ref _fileStreamCount) == 0)
				CleanUpFileStreamDestruction();
			throw new FileBeingDeletedException();
		}

		return new(_handle, _transform.Read, IsRemote);

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
			if (_cacheStatus is CacheStatus.Uncaching || _selfdestructin54321)
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

	public ValueTask<TFChunkBulkReader> AcquireDataReader(CancellationToken token) {
		return TryAcquireBulkMemReader(raw: false, out var reader)
			? ValueTask.FromResult(reader)
			: AcquireFileReader(raw: false, token);
	}

	public ValueTask<TFChunkBulkReader> AcquireRawReader(CancellationToken token) {
		return TryAcquireBulkMemReader(raw: true, out var reader)
			? ValueTask.FromResult(reader)
			: AcquireFileReader(raw: true, token);
	}

	private async ValueTask<TFChunkBulkReader> AcquireFileReader(bool raw, CancellationToken token) {
		Interlocked.Increment(ref _fileStreamCount);
		if (_selfdestructin54321) {
			if (Interlocked.Decrement(ref _fileStreamCount) == 0) {
				CleanUpFileStreamDestruction();
			}

			throw new FileBeingDeletedException();
		}

		// if we get here, then we reserved TFChunk for sure so no one should dispose of chunk file
		// until client returns dedicated reader
		var stream = await CreateFileStreamForBulkReader(token);

		if (raw) {
			return new TFChunkBulkRawReader(this, stream, isMemory: false);
		}

		var streamToUse = _transform.Read.TransformData(new ChunkDataReadStream(stream));
		return new TFChunkBulkDataReader(this, streamToUse, isMemory: false);
	}

	private unsafe UnmanagedMemoryStream CreateMemoryStream(int length, FileAccess access = FileAccess.Read) =>
		new((byte*)_cachedData, length, length, access);

	private async ValueTask<Stream> CreateFileStreamForBulkReader(CancellationToken token) {
		if (_inMem)
			return CreateMemoryStream(_fileSize);

		var handle = await _fileSystem.OpenForReadAsync(
			ChunkLocator,
			IChunkFileSystem.ReadOptimizationHint.SequentialScan,
			token);

		return new PoolingBufferedStream(handle.CreateStream(leaveOpen: false)) {
			MaxBufferSize = 65536
		};
	}

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
			if (!_cachedDataLock.TryAcquire())
				return false;

			try {
				return TryCreateBulkMemReaderUnsafe(raw, out reader);
			} finally {
				_cachedDataLock.Release();
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

	// 'unsafe' because _cachedDataLock must be acquired first
	private bool TryCreateBulkMemReaderUnsafe(bool raw, out TFChunkBulkReader reader) {
		Debug.Assert(_cachedDataLock.IsLockHeld);

		if (_cacheStatus is not CacheStatus.Cached) {
			reader = null;
			return false;
		}

		if (raw && !_cachedDataTransformed) {
			// we want a raw reader for a cached chunk (which should return transformed data)
			// but the cached data is not transformed so we can't use it directly.
			// (likely this chunk was cached before it was completed)
			reader = null;
			return false;
		}

		if (_cachedData is 0)
			throw new Exception("Unexpected error: a cached chunk had no cached data");

		// After incrementing the count we must return true
		Interlocked.Increment(ref _memStreamCount);
		var stream = CreateMemoryStream(_cachedLength);

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

	// creates a bulk reader over a memstream as long as we are cached
	private bool TryCreateBulkMemReader(bool raw, out TFChunkBulkReader reader) {
		_cachedDataLock.TryAcquire(InfiniteTimeSpan);
		try {
			return TryCreateBulkMemReaderUnsafe(raw, out reader);
		} finally {
			_cachedDataLock.Release();
		}
	}

	public void ReleaseReader(TFChunkBulkReader reader) {
		if (reader.IsMemory) {
			switch (Interlocked.Decrement(ref _memStreamCount)) {
				case < 0:
					throw new Exception("Count of mem streams reduced below zero.");
				case 0:
					TryDestructMemStreams();
					break;
			}

			return;
		}

		switch (Interlocked.Decrement(ref _fileStreamCount)) {
			case < 0:
				throw new Exception("Count of file streams reduced below zero.");
			case 0 when _selfdestructin54321:
				CleanUpFileStreamDestruction();
				break;
		}
	}

	public async IAsyncEnumerable<IChunkBlob> UnmergeAsync([EnumeratorCancellation] CancellationToken token) {
		if (ChunkHeader.ChunkStartNumber == ChunkHeader.ChunkEndNumber)
			throw new InvalidOperationException($"Chunk: {ChunkLocator} is not merged");

		if (!ChunkHeader.IsScavenged)
			throw new InvalidOperationException($"Chunk: {ChunkLocator} is not scavenged");

		if (!IsReadOnly)
			throw new InvalidOperationException($"Chunk: {ChunkLocator} is not complete");

		var recordReadResult = await TryReadClosestForward(0L, token);

		for (var logicalChunkNumber = ChunkHeader.ChunkStartNumber;
			 logicalChunkNumber <= ChunkHeader.ChunkEndNumber;
			 logicalChunkNumber++) {

			var unmerged = await Spawn(logicalChunkNumber);
			var writer = new ScavengedChunkWriter(unmerged);

			while (recordReadResult.Success) {
				var recordChunkNumber = (int)(recordReadResult.LogRecord.LogPosition / ChunkHeader.ChunkSize);
				if (recordChunkNumber != logicalChunkNumber)
					break;
				await writer.WriteRecord(recordReadResult.LogRecord, token);
				recordReadResult = await TryReadClosestForward(recordReadResult.NextPosition, token);
			}

			await writer.Complete(token);
			yield return unmerged;
		}

		ValueTask<TFChunk> Spawn(int logicalChunkNumber) => CreateNew(
			_fileSystem,
			filename: Path.Combine(Path.GetDirectoryName(_filename)!, Guid.NewGuid() + ".unmerge.tmp"),
			chunkDataSize: _chunkHeader.ChunkSize,
			chunkStartNumber: logicalChunkNumber,
			chunkEndNumber: logicalChunkNumber,
			isScavenged: true,
			inMem: _inMem,
			unbuffered: _unbuffered,
			writethrough: _writeThrough,
			reduceFileCachePressure: _reduceFileCachePressure,
			tracker: new TFChunkTracker.NoOp(),
			getTransformFactory: _getTransformFactory,
			token);
	}

	async ValueTask<IChunkRawReader> IChunkBlob.AcquireRawReader(CancellationToken token)
		=> (IChunkRawReader)await AcquireRawReader(token);

	public override string ToString() {
		return string.Format("#{0}-{1} ({2})", _chunkHeader.ChunkStartNumber, _chunkHeader.ChunkEndNumber,
			Path.GetFileName(ChunkLocator));
	}

	[StructLayout(LayoutKind.Auto)]
	private readonly struct Midpoint(int itemIndex, in PosMap posmap) {
		public readonly int ItemIndex = itemIndex;
		public readonly long LogPos = posmap.LogPos;

		public override string ToString() => $"ItemIndex: {ItemIndex}, LogPos: {LogPos}";
	}

	// all public members are thread-safe
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
		private static ref ReaderWorkItem UnsafeGetElement(ReaderWorkItem[] array, int index)
		{
			Debug.Assert((uint)index < (uint)array.Length);

			return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), index);
		}

		// releases all available slots in the pool
		public int Drain(ref int referenceCount) {
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

		public bool TryTake(out Slot slot) {
			if (_array is { } array && _indices.TryTake(out int index)) {
				slot = new(array, index);
				return true;
			}

			slot = default;
			return false;
		}

		public bool Return(ReaderWorkItem item) {
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
