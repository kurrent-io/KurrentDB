#pragma warning disable 420

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Exceptions;
using EventStore.Core.Settings;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.Util;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using EventStore.Core.TransactionLog.Unbuffered;
using ILogger = Serilog.ILogger;
using MD5 = EventStore.Core.Hashing.MD5;


namespace EventStore.Core.TransactionLog.Chunks.TFChunk {
	public unsafe partial class TFChunk : IDisposable {
		public enum ChunkVersions : byte {
			OriginalNotUsed = 1,
			Unaligned = 2,
			Aligned = 3
		}

		public const byte CurrentChunkVersion = 3;
		public const int WriteBufferSize = 8192;
		public const int ReadBufferSize = 8192;

		private static readonly ILogger Log = Serilog.Log.ForContext<TFChunk>();

		public bool IsReadOnly {
			get { return Interlocked.CompareExchange(ref _isReadOnly, 0, 0) == 1; }
			set { Interlocked.Exchange(ref _isReadOnly, value ? 1 : 0); }
		}

		public bool IsCached {
			get { return _cacheStatus == CacheStatus.Cached; }
		}

		// the logical size of data (could be > PhysicalDataSize if scavenged chunk)
		public long LogicalDataSize {
			get { return Interlocked.Read(ref _logicalDataSize); }
		}

		// the physical size of data size of data
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

		public readonly int MidpointsDepth;

		public int RawWriterPosition {
			get {
				var writerWorkItem = _writerWorkItem;
				if (writerWorkItem == null)
					throw new InvalidOperationException(string.Format("TFChunk {0} is not in write mode.", _filename));
				return (int)writerWorkItem.StreamPosition;
			}
		}

		private readonly bool _inMem;
		private readonly string _filename;
		private int _fileSize;
		private int _isReadOnly;
		private ChunkHeader _chunkHeader;
		private ChunkFooter _chunkFooter;

		private readonly int _maxReaderCount;
		private readonly ConcurrentQueue<ReaderWorkItem> _fileStreams = new ConcurrentQueue<ReaderWorkItem>();
		private readonly ConcurrentQueue<ReaderWorkItem> _memStreams = new ConcurrentQueue<ReaderWorkItem>();
		private int _internalStreamsCount;
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
		private volatile IntPtr _cachedData;
		private int _cachedLength;
		private volatile CacheStatus _cacheStatus;

		private enum CacheStatus {
			// The default state.
			// CacheInMemory can transition us to Cached
			// invariants: _cachedData == IntPtr.Zero, _memStreamCount == 0
			Uncached = 0,

			// UnCacheFromMemory can transition us to Uncaching
			// invariants: _cachedData != IntPtr.Zero, _memStreamCount == _maxReaderCount
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
		private bool _unbuffered;
		private bool _writeThrough;
		private readonly bool _reduceFileCachePressure;
		private readonly IChunkCacheManager _cacheManager;

		private IChunkReadSide _readSide;

		private TFChunk(
			IChunkCacheManager cacheManager,
			string filename,
			int initialReaderCount,
			int maxReaderCount,
			int midpointsDepth,
			bool inMem,
			bool unbuffered,
			bool writethrough,
			bool reduceFileCachePressure) {
			Ensure.NotNullOrEmpty(filename, "filename");
			Ensure.Positive(initialReaderCount, "initialReaderCount");
			Ensure.Positive(maxReaderCount, "maxReaderCount");
			if (initialReaderCount > maxReaderCount)
				throw new ArgumentOutOfRangeException("initialReaderCount",
					"initialReaderCount is greater than maxReaderCount.");
			Ensure.Nonnegative(midpointsDepth, "midpointsDepth");

			_cacheManager = cacheManager;
			_filename = filename;
			_internalStreamsCount = initialReaderCount;
			_maxReaderCount = maxReaderCount;
			MidpointsDepth = midpointsDepth;
			_inMem = inMem;
			_unbuffered = unbuffered;
			_writeThrough = writethrough;
			_reduceFileCachePressure = reduceFileCachePressure;
		}

		~TFChunk() {
			FreeCachedData();
		}

		public static TFChunk FromCompletedFile(IChunkCacheManager cacheManager, string filename, bool verifyHash, bool unbufferedRead,
			int initialReaderCount, int maxReaderCount, ITransactionFileTracker tracker, bool optimizeReadSideCache = false, bool reduceFileCachePressure = false) {
			var chunk = new TFChunk(cacheManager, filename, initialReaderCount, maxReaderCount,
				TFConsts.MidpointsDepth, false, unbufferedRead, false, reduceFileCachePressure);
			try {
				chunk.InitCompleted(verifyHash, optimizeReadSideCache, tracker);
			} catch {
				chunk.Dispose();
				throw;
			}

			return chunk;
		}

		public static TFChunk FromOngoingFile(IChunkCacheManager cacheManager, string filename, int writePosition, bool checkSize, bool unbuffered,
			bool writethrough, int initialReaderCount, int maxReaderCount, bool reduceFileCachePressure, ITransactionFileTracker tracker) {
			var chunk = new TFChunk(cacheManager, filename,
				initialReaderCount,
				maxReaderCount,
				TFConsts.MidpointsDepth,
				false,
				unbuffered,
				writethrough, reduceFileCachePressure);
			try {
				chunk.InitOngoing(writePosition, checkSize, tracker);
			} catch {
				chunk.Dispose();
				throw;
			}

			return chunk;
		}

		public static TFChunk CreateNew(
			IChunkCacheManager cacheManager,
			string filename,
			int chunkSize,
			int chunkStartNumber,
			int chunkEndNumber,
			bool isScavenged,
			bool inMem,
			bool unbuffered,
			bool writethrough,
			int initialReaderCount,
			int maxReaderCount,
			bool reduceFileCachePressure,
			ITransactionFileTracker tracker) {
			var size = GetAlignedSize(chunkSize + ChunkHeader.Size + ChunkFooter.Size);
			var chunkHeader = new ChunkHeader(CurrentChunkVersion, chunkSize, chunkStartNumber, chunkEndNumber,
				isScavenged, Guid.NewGuid());
			return CreateWithHeader(cacheManager, filename, chunkHeader, size, inMem, unbuffered, writethrough, initialReaderCount, maxReaderCount,
				reduceFileCachePressure, tracker);
		}

		public static TFChunk CreateWithHeader(
			IChunkCacheManager cacheManager,
			string filename,
			ChunkHeader header,
			int fileSize,
			bool inMem,
			bool unbuffered,
			bool writethrough,
			int initialReaderCount,
			int maxReaderCount,
			bool reduceFileCachePressure,
			ITransactionFileTracker tracker) {
			var chunk = new TFChunk(cacheManager, filename,
				initialReaderCount,
				maxReaderCount,
				TFConsts.MidpointsDepth,
				inMem,
				unbuffered,
				writethrough,
				reduceFileCachePressure);
			try {
				chunk.InitNew(header, fileSize, tracker);
			} catch {
				chunk.Dispose();
				throw;
			}

			return chunk;
		}

		private void InitCompleted(bool verifyHash, bool optimizeReadSideCache, ITransactionFileTracker tracker) {
			var fileInfo = new FileInfo(_filename);
			if (!fileInfo.Exists)
				throw new CorruptDatabaseException(new ChunkNotFoundException(_filename));

			_fileSize = (int)fileInfo.Length;
			IsReadOnly = true;
			SetAttributes(_filename, true);
			CreateReaderStreams();

			var reader = GetReaderWorkItem();
			try {
				_chunkHeader = ReadHeader(reader.Stream);
				Log.Debug("Opened completed {chunk} as version {version}", _filename, _chunkHeader.Version);
				if (_chunkHeader.Version != (byte)ChunkVersions.Unaligned &&
				    _chunkHeader.Version != (byte)ChunkVersions.Aligned)
					throw new CorruptDatabaseException(new WrongFileVersionException(_filename, _chunkHeader.Version,
						CurrentChunkVersion));

				if (_chunkHeader.Version != (byte)ChunkVersions.Aligned && _unbuffered) {
					throw new Exception(
						"You can only run unbuffered mode on v3 or higher chunk files. Please run scavenge on your database to upgrade your transaction file to v3.");
				}

				_chunkFooter = ReadFooter(reader.Stream);
				if (!_chunkFooter.IsCompleted) {
					throw new CorruptDatabaseException(new BadChunkInDatabaseException(
						string.Format("Chunk file '{0}' should be completed, but is not.", _filename)));
				}

				_logicalDataSize = _chunkFooter.LogicalDataSize;
				_physicalDataSize = _chunkFooter.PhysicalDataSize;
				var expectedFileSize = _chunkFooter.PhysicalDataSize + _chunkFooter.MapSize + ChunkHeader.Size +
				                       ChunkFooter.Size;
				if (_chunkHeader.Version == (byte)ChunkVersions.Unaligned && reader.Stream.Length != expectedFileSize) {
					throw new CorruptDatabaseException(new BadChunkInDatabaseException(
						string.Format(
							"Chunk file '{0}' should have a file size of {1} bytes, but it has a size of {2} bytes.",
							_filename,
							expectedFileSize,
							reader.Stream.Length)));
				}
			} finally {
				ReturnReaderWorkItem(reader);
			}

			_readSide = _chunkHeader.IsScavenged
				? (IChunkReadSide)new TFChunkReadSideScavenged(this, optimizeReadSideCache, tracker)
				: new TFChunkReadSideUnscavenged(this, tracker);

			// do not actually cache now because it is too slow when opening the database
			_readSide.RequestCaching();

			if (verifyHash)
				VerifyFileHash();
		}

		private void InitNew(ChunkHeader chunkHeader, int fileSize, ITransactionFileTracker tracker) {
			Ensure.NotNull(chunkHeader, "chunkHeader");
			Ensure.Positive(fileSize, "fileSize");

			_fileSize = fileSize;
			IsReadOnly = false;
			_chunkHeader = chunkHeader;
			_physicalDataSize = 0;
			_logicalDataSize = 0;

			if (_inMem)
				CreateInMemChunk(chunkHeader, fileSize);
			else {
				CreateWriterWorkItemForNewChunk(chunkHeader, fileSize);
				SetAttributes(_filename, false);
			}

			_readSide = chunkHeader.IsScavenged
				? (IChunkReadSide)new TFChunkReadSideScavenged(this, false, tracker)
				: new TFChunkReadSideUnscavenged(this, tracker);

			// Always cache the active chunk
			// If the chunk is scavenged we will definitely mark it readonly before we are done writing to it.
			if (!chunkHeader.IsScavenged) {
				CacheInMemory();
			}
		}

		private void InitOngoing(int writePosition, bool checkSize, ITransactionFileTracker tracker) {
			Ensure.Nonnegative(writePosition, "writePosition");
			var fileInfo = new FileInfo(_filename);
			if (!fileInfo.Exists)
				throw new CorruptDatabaseException(new ChunkNotFoundException(_filename));

			_fileSize = (int)fileInfo.Length;
			IsReadOnly = false;
			_physicalDataSize = writePosition;
			_logicalDataSize = writePosition;

			SetAttributes(_filename, false);
			CreateWriterWorkItemForExistingChunk(writePosition, out _chunkHeader);
			Log.Debug("Opened ongoing {chunk} as version {version}", _filename, _chunkHeader.Version);
			if (_chunkHeader.Version != (byte)ChunkVersions.Aligned &&
			    _chunkHeader.Version != (byte)ChunkVersions.Unaligned)
				throw new CorruptDatabaseException(new WrongFileVersionException(_filename, _chunkHeader.Version,
					CurrentChunkVersion));

			if (checkSize) {
				var expectedFileSize = _chunkHeader.ChunkSize + ChunkHeader.Size + ChunkFooter.Size;
				if (_writerWorkItem.StreamLength != expectedFileSize) {
					throw new CorruptDatabaseException(new BadChunkInDatabaseException(
						string.Format(
							"Chunk file '{0}' should have file size {1} bytes, but instead has {2} bytes length.",
							_filename,
							expectedFileSize,
							_writerWorkItem.StreamLength)));
				}
			}

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
			Interlocked.Add(ref _fileStreamCount, _internalStreamsCount);

			if (_selfdestructin54321)
				throw new FileBeingDeletedException();

			for (int i = 0; i < _internalStreamsCount; i++) {
				_fileStreams.Enqueue(CreateInternalReaderWorkItem());
			}
		}

		private ReaderWorkItem CreateInternalReaderWorkItem() {
			Stream stream;
			if (_unbuffered) {
				stream = UnbufferedFileStream.Create(
					_filename,
					FileMode.Open,
					FileAccess.Read,
					FileShare.ReadWrite,
					false,
					1024 * 1024,
					4096,
					false,
					4096);
			} else {
				stream = new FileStream(
					_filename,
					FileMode.Open,
					FileAccess.Read,
					FileShare.ReadWrite,
					ReadBufferSize,
					_reduceFileCachePressure ? FileOptions.None : FileOptions.RandomAccess);
			}

			var reader = new BinaryReader(stream);
			return new ReaderWorkItem(stream, reader, false);
		}

		private void CreateInMemChunk(ChunkHeader chunkHeader, int fileSize) {
			var md5 = MD5.Create();

			// ALLOCATE MEM
			_cacheStatus = CacheStatus.Cached;
			_cachedLength = fileSize;
			_cachedData = _cacheManager.AllocateAtLeast(_cachedLength);

			// WRITER STREAM
			var memStream =
				new UnmanagedMemoryStream((byte*)_cachedData, _cachedLength, _cachedLength, FileAccess.ReadWrite);
			WriteHeader(md5, memStream, chunkHeader);
			memStream.Position = ChunkHeader.Size;

			// READER STREAMS
			Interlocked.Add(ref _memStreamCount, _maxReaderCount);

			// should never happen in practice because this function is called from the static TFChunk constructors
			Debug.Assert(!_selfdestructin54321);

			for (int i = 0; i < _maxReaderCount; i++) {
				var stream = new UnmanagedMemoryStream((byte*)_cachedData, _cachedLength);
				var reader = new BinaryReader(stream);
				_memStreams.Enqueue(new ReaderWorkItem(stream, reader, isMemory: true));
			}

			_writerWorkItem = new WriterWorkItem(null, memStream, md5);
		}

		private void CreateWriterWorkItemForNewChunk(ChunkHeader chunkHeader, int fileSize) {
			var md5 = MD5.Create();

			// create temp file first and set desired length
			// if there is not enough disk space or something else prevents file to be resized as desired
			// we'll end up with empty temp file, which won't trigger false error on next DB verification
			var tempFilename = string.Format("{0}.{1}.tmp", _filename, Guid.NewGuid());
			var tempFile = new FileStream(tempFilename, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read,
				WriteBufferSize, FileOptions.SequentialScan);
			tempFile.SetLength(fileSize);

			// we need to write header into temp file before moving it into correct chunk place, so in case of crash
			// we don't end up with seemingly valid chunk file with no header at all...
			WriteHeader(md5, tempFile, chunkHeader);

			tempFile.FlushToDisk();
			tempFile.Close();
			File.Move(tempFilename, _filename);
			Stream stream = GetWriteStream(_filename);
			stream.Position = ChunkHeader.Size;
			_writerWorkItem = new WriterWorkItem(stream, null, md5);
			Flush(); // persist file move result
		}

		private Stream GetWriteStream(string filename) {
			if (!_unbuffered) {
				return new FileStream(
					_filename,
					FileMode.Open,
					FileAccess.ReadWrite,
					FileShare.Read,
					WriteBufferSize,
					FileOptions.SequentialScan);
			} else {
				Log.Debug("Using unbuffered access for TFChunk '{chunk}'...", _filename);
				return UnbufferedFileStream.Create(
					_filename,
					FileMode.Open,
					FileAccess.ReadWrite,
					FileShare.Read,
					false,
					4096 * 1024,
					4096,
					_writeThrough,
					4096);
			}
		}

		private void CreateWriterWorkItemForExistingChunk(int writePosition, out ChunkHeader chunkHeader) {
			var md5 = MD5.Create();
			var stream = GetWriteStream(_filename);
			try {
				chunkHeader = ReadHeader(stream);
				if (chunkHeader.Version == (byte)ChunkVersions.Unaligned) {
					Log.Debug("Upgrading ongoing file {chunk} to version 3", _filename);
					var newHeader = new ChunkHeader((byte)ChunkVersions.Aligned,
						chunkHeader.ChunkSize,
						chunkHeader.ChunkStartNumber,
						chunkHeader.ChunkEndNumber,
						false,
						chunkHeader.ChunkId);
					stream.Seek(0, SeekOrigin.Begin);
					chunkHeader = newHeader;
					var head = newHeader.AsByteArray();
					stream.Write(head, 0, head.Length);
					stream.Flush();
					stream.Seek(0, SeekOrigin.Begin);
				}
			} catch {
				stream.Dispose();
				((IDisposable)md5).Dispose();
				throw;
			}

			var realPosition = GetRawPosition(writePosition);
			MD5Hash.ContinuousHashFor(md5, stream, 0, realPosition);
			stream.Position = realPosition; // this reordering fixes bug in Mono implementation of FileStream
			_writerWorkItem = new WriterWorkItem(stream, null, md5);
		}

		private void WriteHeader(HashAlgorithm md5, Stream stream, ChunkHeader chunkHeader) {
			var chunkHeaderBytes = chunkHeader.AsByteArray();
			md5.TransformBlock(chunkHeaderBytes, 0, ChunkHeader.Size, null, 0);
			stream.Write(chunkHeaderBytes, 0, ChunkHeader.Size);
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
			using (var reader = AcquireReader()) {
				reader.Stream.Seek(0, SeekOrigin.Begin);
				var stream = reader.Stream;
				var footer = _chunkFooter;

				byte[] hash;
				using (var md5 = MD5.Create()) {
					// hash header and data
					MD5Hash.ContinuousHashFor(md5, stream, 0, ChunkHeader.Size + footer.PhysicalDataSize);
					// hash mapping and footer except MD5 hash sum which should always be last
					MD5Hash.ContinuousHashFor(md5,
						stream,
						ChunkHeader.Size + footer.PhysicalDataSize,
						stream.Length - ChunkHeader.Size - footer.PhysicalDataSize - ChunkFooter.ChecksumSize);
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
			return workItem.StreamPosition - ChunkHeader.Size;
		}

		// There are four kinds of event position
		// (a) global logical (logical position in the log)
		// (b) local logical (logical position in the chunk, which is global logical - chunk logical start)
		// (c) actual (local logical but with posmap taken into account)
		// (d) raw (byte offset in file, which is actual - header size)
		//
		// this method takes (b) and returns (d)
		public long GetActualRawPosition(long logicalPosition) {
			if (logicalPosition < 0)
				throw new ArgumentOutOfRangeException(nameof(logicalPosition));

			var actualPosition = _readSide.GetActualPosition(logicalPosition);

			if (actualPosition < 0)
				return -1;

			return GetRawPosition(actualPosition);
		}

		public void CacheInMemory() {
			lock (_cachedDataLock) {
				if (_inMem)
					return;

				if (_cacheStatus != CacheStatus.Uncached) {
					// expected to be very rare
					if (_cacheStatus == CacheStatus.Uncaching)
						Log.Debug("CACHING TFChunk {chunk} SKIPPED because it is uncaching.", this);

					return;
				}

				// we won the right to cache
				var sw = Stopwatch.StartNew();
				try {
					BuildCacheArray();
				} catch (OutOfMemoryException) {
					Log.Error("CACHING FAILED due to OutOfMemory exception in TFChunk {chunk}.", this);
					return;
				} catch (FileBeingDeletedException) {
					Log.Debug(
						"CACHING FAILED due to FileBeingDeleted exception (TFChunk is being disposed) in TFChunk {chunk}.",
						this);
					return;
				}

				Interlocked.Add(ref _memStreamCount, _maxReaderCount);
				if (_selfdestructin54321) {
					if (Interlocked.Add(ref _memStreamCount, -_maxReaderCount) == 0)
						FreeCachedData();
					Log.Debug("CACHING ABORTED for TFChunk {chunk} as TFChunk was probably marked for deletion.", this);
					return;
				}

				var writerWorkItem = _writerWorkItem;
				if (writerWorkItem != null) {
					var memStream = new UnmanagedMemoryStream((byte*)_cachedData, _cachedLength, _cachedLength,
						FileAccess.ReadWrite);
					memStream.Position = writerWorkItem.StreamPosition;
					writerWorkItem.SetMemStream(memStream);
				}

				for (int i = 0; i < _maxReaderCount; i++) {
					var stream = new UnmanagedMemoryStream((byte*)_cachedData, _cachedLength);
					var reader = new BinaryReader(stream);
					_memStreams.Enqueue(new ReaderWorkItem(stream, reader, isMemory: true));
				}

				_readSide.Uncache();

				Log.Debug("CACHED TFChunk {chunk} in {elapsed}.", this, sw.Elapsed);

				if (_selfdestructin54321)
					TryDestructMemStreams();

				_cacheStatus = CacheStatus.Cached;
			}
		}

		private void BuildCacheArray() {
			var workItem = AcquireFileReader();
			try {
				if (workItem.IsMemory)
					throw new InvalidOperationException(
						"When trying to build cache, reader worker is already in-memory reader.");

				var dataSize = IsReadOnly ? _physicalDataSize + ChunkFooter.MapSize : _chunkHeader.ChunkSize;
				_cachedLength = GetAlignedSize(ChunkHeader.Size + dataSize + ChunkFooter.Size);
				var cachedData = _cacheManager.AllocateAtLeast(_cachedLength);

				try {
					using (var unmanagedStream = new UnmanagedMemoryStream((byte*)cachedData, _cachedLength,
						_cachedLength, FileAccess.ReadWrite)) {
						workItem.Stream.Seek(0, SeekOrigin.Begin);
						var buffer = new byte[65536];
						// in ongoing chunk there is no need to read everything, it's enough to read just actual data written
						int toRead = IsReadOnly ? _cachedLength : ChunkHeader.Size + _physicalDataSize;
						while (toRead > 0) {
							int read = workItem.Stream.Read(buffer, 0, Math.Min(toRead, buffer.Length));
							if (read == 0)
								break;
							toRead -= read;
							unmanagedStream.Write(buffer, 0, read);
						}
					}
				} catch {
					_cacheManager.Free(cachedData);
					throw;
				}

				_cachedData = cachedData;
			} finally {
				workItem.Dispose();
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

					var writerWorkItem = _writerWorkItem;
					if (writerWorkItem != null)
						writerWorkItem.DisposeMemStream();

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
			if (!ChunkHeader.IsScavenged) return;
			((TFChunkReadSideScavenged)_readSide).OptimizeExistsAt();
		}

		public void DeOptimizeExistsAt() {
			if (!ChunkHeader.IsScavenged) return;
			((TFChunkReadSideScavenged)_readSide).DeOptimizeExistsAt();
		}

		public RecordReadResult TryReadAt(long logicalPosition, bool couldBeScavenged) {
			return _readSide.TryReadAt(logicalPosition, couldBeScavenged);
		}

		public RecordReadResult TryReadFirst() {
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

			if (workItem.StreamPosition + length + 2 * sizeof(int) > ChunkHeader.Size + _chunkHeader.ChunkSize)
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

		public bool TryAppendRawData(byte[] buffer) {
			var workItem = _writerWorkItem;
			if (workItem.StreamPosition + buffer.Length > workItem.StreamLength)
				return false;
			WriteRawData(workItem, buffer, buffer.Length);
			return true;
		}

		private static long WriteRawData(WriterWorkItem workItem, MemoryStream buffer) {
			var len = (int)buffer.Length;
			var buf = buffer.GetBuffer();
			return WriteRawData(workItem, buf, len);
		}

		private static long WriteRawData(WriterWorkItem workItem, byte[] buf, int len) {
			var curPos = GetDataPosition(workItem);
			workItem.MD5.TransformBlock(buf, 0, len, null, 0);
			workItem.AppendData(buf, 0, len);
			return curPos;
		}

		public void Flush() {
			if (_inMem)
				return;
			if (IsReadOnly)
				throw new InvalidOperationException("Cannot write to a read-only TFChunk.");
			_writerWorkItem.FlushToDisk();
		}

		public void Complete() {
			if (ChunkHeader.IsScavenged)
				throw new InvalidOperationException("CompleteScavenged should be used for scavenged chunks.");
			CompleteNonRaw(null);
		}

		public void CompleteScavenge(ICollection<PosMap> mapping) {
			if (!ChunkHeader.IsScavenged)
				throw new InvalidOperationException("CompleteScavenged should not be used for non-scavenged chunks.");

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

			CleanUpWriterWorkItem(_writerWorkItem);
			_writerWorkItem = null;
			SetAttributes(_filename, true);
		}

		public void CompleteRaw() {
			if (IsReadOnly)
				throw new InvalidOperationException("Cannot complete a read-only TFChunk.");
			if (_writerWorkItem.StreamPosition != _writerWorkItem.StreamLength)
				throw new InvalidOperationException("The raw chunk is not completely written.");
			Flush();

			if (!_inMem)
				CreateReaderStreams();

			_chunkFooter = ReadFooter(_writerWorkItem.WorkingStream);
			IsReadOnly = true;

			CleanUpWriterWorkItem(_writerWorkItem);
			_writerWorkItem = null;
			SetAttributes(_filename, true);
		}

		private ChunkFooter WriteFooter(ICollection<PosMap> mapping) {
			var workItem = _writerWorkItem;
			workItem.ResizeStream((int)workItem.StreamPosition);

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

			if (_chunkHeader.Version >= (byte)ChunkVersions.Aligned) {
				var alignedSize = GetAlignedSize(ChunkHeader.Size + _physicalDataSize + mapSize + ChunkFooter.Size);
				var bufferSize = alignedSize - workItem.StreamPosition - ChunkFooter.Size;
				Log.Debug("Buffer size is {bufferSize}", bufferSize);
				if (bufferSize > 0) {
					byte[] buffer = new byte[bufferSize];
					WriteRawData(workItem, buffer, buffer.Length);
				}
			}

			Flush();

			var footerNoHash = new ChunkFooter(true, true, _physicalDataSize, LogicalDataSize, mapSize,
				new byte[ChunkFooter.ChecksumSize]);
			//MD5
			workItem.MD5.TransformFinalBlock(footerNoHash.AsByteArray(), 0,
				ChunkFooter.Size - ChunkFooter.ChecksumSize);
			//FILE
			var footerWithHash =
				new ChunkFooter(true, true, _physicalDataSize, LogicalDataSize, mapSize, workItem.MD5.Hash);
			workItem.AppendData(footerWithHash.AsByteArray(), 0, ChunkFooter.Size);

			Flush();

			_fileSize = (int)workItem.StreamLength;
			return footerWithHash;
		}

		private void CleanUpWriterWorkItem(WriterWorkItem writerWorkItem) {
			if (writerWorkItem == null)
				return;
			writerWorkItem.Dispose();
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
			int fileStreamCount = Interlocked.CompareExchange(ref _fileStreamCount, 0, 0);

			ReaderWorkItem workItem;
			while (_fileStreams.TryDequeue(out workItem)) {
				workItem.Stream.Dispose();
				fileStreamCount = Interlocked.Decrement(ref _fileStreamCount);
			}

			if (fileStreamCount < 0)
				throw new Exception("Count of file streams reduced below zero.");

			if (fileStreamCount == 0) {
				CleanUpFileStreamDestruction();
				return true;
			}

			return false;
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

			CleanUpWriterWorkItem(_writerWorkItem);

			if (!_inMem) {
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
			if (size % 4096 == 0) return size;
			return (size / 4096 + 1) * 4096;
		}

		private bool TryDestructMemStreams() {
			lock (_cachedDataLock) {
				if (_cacheStatus != CacheStatus.Uncaching && !_selfdestructin54321)
					return false;

				var writerWorkItem = _writerWorkItem;
				if (writerWorkItem != null)
					writerWorkItem.DisposeMemStream();

				int memStreamCount = Interlocked.CompareExchange(ref _memStreamCount, 0, 0);

				ReaderWorkItem workItem;
				while (_memStreams.TryDequeue(out workItem)) {
					memStreamCount = Interlocked.Decrement(ref _memStreamCount);
				}

				if (memStreamCount < 0)
					throw new Exception("Count of memory streams reduced below zero.");

				if (memStreamCount == 0) {
					// make sure "the light is off" for memory streams
					FreeCachedData();
					return true;
				}

				return false;
			}
		}

		private void FreeCachedData() {
			lock (_cachedDataLock) {
				var cachedData = _cachedData;
				if (cachedData != IntPtr.Zero) {
					_cacheManager.Free(cachedData);
					_cachedData = IntPtr.Zero;
					_cacheStatus = CacheStatus.Uncached;
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

			ReaderWorkItem item;
			// try get memory stream reader first
			if (_memStreams.TryDequeue(out item))
				return item;

			if (_inMem)
				throw new Exception("Not enough memory streams during in-mem TFChunk mode.");

			if (!IsReadOnly) {
				// chunks cannot be read using filestreams while they can still be written to
				lock (_cachedDataLock) {
					if (_cacheStatus != CacheStatus.Cached)
						throw new Exception("Active chunk must be cached but was not.");
					else
						throw new Exception("Not enough memory streams for active chunk.");
				}
			}

			// get a filestream from the pool, or create one if the pool is empty.
			if (_fileStreams.TryDequeue(out item))
				return item;

			if (_selfdestructin54321)
				throw new FileBeingDeletedException();

			var internalStreamCount = Interlocked.Increment(ref _internalStreamsCount);
			if (internalStreamCount > _maxReaderCount)
				throw new Exception("Unable to acquire reader work item. Max reader count reached.");

			Interlocked.Increment(ref _fileStreamCount);
			if (_selfdestructin54321) {
				if (Interlocked.Decrement(ref _fileStreamCount) == 0)
					CleanUpFileStreamDestruction();
				throw new FileBeingDeletedException();
			}

			// if we get here, then we reserved TFChunk for sure so no one should dispose of chunk file
			// until client returns the reader - if we successfully create one.
			// creating the reader might fail because of reaching the file handle limit
			try {
				return CreateInternalReaderWorkItem();
			} catch {
				Interlocked.Decrement(ref _internalStreamsCount);

				var fileStreamCount = Interlocked.Decrement(ref _fileStreamCount);
				if (_selfdestructin54321 && fileStreamCount == 0)
					CleanUpFileStreamDestruction();

				throw;
			}
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
				_memStreams.Enqueue(item);
				Thread.MemoryBarrier();
				if (_cacheStatus == CacheStatus.Uncaching || _selfdestructin54321)
					TryDestructMemStreams();
			} else {
				_fileStreams.Enqueue(item);
				if (_selfdestructin54321)
					TryDestructFileStreams();
			}
		}

		public TFChunkBulkReader AcquireReader() {
			if (TryAcquireBulkMemReader(out var reader))
				return reader;

			return AcquireFileReader();
		}

		private TFChunkBulkReader AcquireFileReader() {
			Interlocked.Increment(ref _fileStreamCount);
			if (_selfdestructin54321) {
				if (Interlocked.Decrement(ref _fileStreamCount) == 0) {
					CleanUpFileStreamDestruction();
				}

				throw new FileBeingDeletedException();
			}

			// if we get here, then we reserved TFChunk for sure so no one should dispose of chunk file
			// until client returns dedicated reader
			return new TFChunkBulkReader(this, GetSequentialReaderFileStream(), isMemory: false);
		}

		private Stream GetSequentialReaderFileStream() {
			return _inMem
				? (Stream)new UnmanagedMemoryStream((byte*)_cachedData, _fileSize)
				: new FileStream(_filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536,
					FileOptions.SequentialScan);
		}

		// tries to acquire a bulk reader over a memstream but
		// (a) doesn't block if a file reader would be acceptable instead
		//     (we might be in the middle of caching which could take a while)
		// (b) _does_ throw if we can't get a memstream and a filestream is not acceptable
		private bool TryAcquireBulkMemReader(out TFChunkBulkReader reader) {
			reader = null;

			if (IsReadOnly) {
				// chunk is definitely readonly and will remain so, so a filestream would be acceptable.
				// we might be able to get a memstream but we don't want to wait for the lock in case we
				// are currently performing a slow operation with it such as caching.
				if (!Monitor.TryEnter(_cachedDataLock))
					return false;

				try {
					return TryCreateBulkMemReader(out reader);
				} finally {
					Monitor.Exit(_cachedDataLock);
				}
			}

			// chunk is not readonly so it should be cached and let us create a mem reader
			// (but might become readonly at any moment!)
			if (TryCreateBulkMemReader(out reader))
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
		private bool TryCreateBulkMemReader(out TFChunkBulkReader reader) {
			lock (_cachedDataLock) {
				if (_cacheStatus != CacheStatus.Cached) {
					reader = null;
					return false;
				}

				if (_cachedData == IntPtr.Zero)
					throw new Exception("Unexpected error: a cached chunk had no cached data");

				Interlocked.Increment(ref _memStreamCount);
				var stream = new UnmanagedMemoryStream((byte*)_cachedData, _cachedLength);
				reader = new TFChunkBulkReader(this, stream, isMemory: true);
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
	}
}
