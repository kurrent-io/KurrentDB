// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;
using EventStore.Common.Utils;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.Transforms;
using EventStore.Core.Transforms.Identity;
using ChunkInfo = EventStore.Core.Data.ChunkInfo;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.TransactionLog.Chunks;

public sealed class TFChunkManager : IChunkRegistry<TFChunk.TFChunk>, IThreadPoolWorkItem {
	private static readonly ILogger Log = Serilog.Log.ForContext<TFChunkManager>();

	// MaxChunksCount is currently capped at 400,000 since:
	// - the chunk file naming strategy supports only up to 6 digits for the chunk number.
	// - this class uses a fixed size array to keep the chunk list
	public const int MaxChunksCount = 400_000;

	public int ChunksCount {
		get { return _chunksCount; }
	}

	private readonly TFChunkDbConfig _config;
	private readonly TFChunk.TFChunk[] _chunks = new TFChunk.TFChunk[MaxChunksCount];
	private readonly ITransactionFileTracker _tracker;
	private readonly DbTransformManager _transformManager;

	private volatile int _chunksCount;
	private volatile bool _cachingEnabled;

	// protects _chunksCount and _chunks
	private readonly AsyncExclusiveLock _chunksLocker = new();
	private int _backgroundPassesRemaining;
	private int _backgroundRunning;
	public Action<ChunkInfo> OnChunkSwitched { get; init; }

	public TFChunkManager(
		TFChunkDbConfig config,
		IChunkFileSystem fileSystem,
		ITransactionFileTracker tracker,
		DbTransformManager transformManager) {
		Ensure.NotNull(config, "config");
		_config = config;
		_tracker = tracker;
		_transformManager = transformManager;
		FileSystem = fileSystem;
	}

	public IChunkFileSystem FileSystem { get; }

	public async ValueTask EnableCaching(CancellationToken token) {
		await _chunksLocker.AcquireAsync(token);
		try {
			_cachingEnabled = true;
		} finally {
			_chunksLocker.Release();
		}

		// trigger caching out of lock to avoid lock contention
		TriggerBackgroundCaching();
	}

	async void IThreadPoolWorkItem.Execute() {
		do {
			do {
				await CacheUncacheReadOnlyChunks(CancellationToken.None);
			} while (Interlocked.Decrement(ref _backgroundPassesRemaining) > 0);

			Interlocked.Exchange(ref _backgroundRunning, 0);
		} while (Interlocked.CompareExchange(ref _backgroundPassesRemaining, 0, 0) > 0
		         && Interlocked.CompareExchange(ref _backgroundRunning, 1, 0) == 0);
	}

	private async ValueTask CacheUncacheReadOnlyChunks(CancellationToken token) {
		int lastChunkToCache;

		await _chunksLocker.AcquireAsync(token);
		try {
			long totalSize = 0;
			lastChunkToCache = _chunksCount;

			// work backwards through the history until we have found enough chunks
			// to cache according to the chunk cache size. determines lastChunkToCache.
			for (int chunkNum = _chunksCount - 1; chunkNum >= 0;) {
				var chunk = _chunks[chunkNum];
				await chunk.EnsureInitialized(token);
				var chunkSize = chunk.IsReadOnly
					? chunk.ChunkFooter.PhysicalDataSize + chunk.ChunkFooter.MapSize + ChunkHeader.Size +
					  ChunkFooter.Size
					: chunk.ChunkHeader.ChunkSize + ChunkHeader.Size + ChunkFooter.Size;

				if (totalSize + chunkSize > _config.MaxChunksCacheSize)
					break;

				totalSize += chunkSize;
				lastChunkToCache = chunk.ChunkHeader.ChunkStartNumber;

				chunkNum = chunk.ChunkHeader.ChunkStartNumber - 1;
			}
		} finally {
			_chunksLocker.Release();
		}

		// uncache everything earlier than lastChunkToCache
		for (int chunkNum = lastChunkToCache - 1; chunkNum >= 0;) {
			var chunk = _chunks[chunkNum];
			if (chunk.IsReadOnly)
				await chunk.UnCacheFromMemory(token);
			chunkNum = chunk.ChunkInfo.ChunkStartNumber - 1;
		}

		// cache everything from lastChunkToCache up to now
		for (int chunkNum = lastChunkToCache; chunkNum < _chunksCount;) {
			var chunk = _chunks[chunkNum];
			if (chunk.IsReadOnly) {
				await chunk.EnsureInitialized(token);
				await chunk.CacheInMemory(token);
			}

			chunkNum = chunk.ChunkInfo.ChunkEndNumber + 1;
		}
	}

	public ValueTask<TFChunk.TFChunk> CreateTempChunk(ChunkHeader chunkHeader, int fileSize, CancellationToken token) {
		var chunkFileName = FileSystem.LocalNamingStrategy.CreateTempFilename();
		return TFChunk.TFChunk.CreateWithHeader(
			FileSystem,
			chunkFileName,
			chunkHeader,
			fileSize,
			_config.InMemDb,
			_config.Unbuffered,
			_config.WriteThrough,
			_config.ReduceFileCachePressure,
			_tracker,
			// temporary chunks are used for replicating raw (scavenged) chunks.
			// since the raw data being replicated is already transformed, we use
			// the identity transform as we don't want to transform the data again
			// when appending raw data to the chunk.
			transformFactory: new IdentityChunkTransformFactory(),
			getTransformFactory: _transformManager,
			ReadOnlyMemory<byte>.Empty,
			token);
	}

	public async ValueTask<TFChunk.TFChunk> AddNewChunk(CancellationToken token) {
		TFChunk.TFChunk chunk;
		bool triggerCaching;
		await _chunksLocker.AcquireAsync(token);
		try {
			var chunkNumber = _chunksCount;
			var chunkName = FileSystem.LocalNamingStrategy.GetFilenameFor(chunkNumber, 0);
			chunk = await TFChunk.TFChunk.CreateNew(
				FileSystem,
				chunkName,
				_config.ChunkSize,
				chunkNumber,
				chunkNumber,
				isScavenged: false,
				inMem: _config.InMemDb,
				unbuffered: _config.Unbuffered,
				writethrough: _config.WriteThrough,
				reduceFileCachePressure: _config.ReduceFileCachePressure,
				tracker: _tracker,
				getTransformFactory: _transformManager,
				token);
			AddChunk(chunk);
			triggerCaching = _cachingEnabled;
		} finally {
			_chunksLocker.Release();
		}

		// trigger caching out of lock to avoid lock contention
		if (triggerCaching)
			TriggerBackgroundCaching();
		return chunk;
	}

	public async ValueTask<TFChunk.TFChunk> AddNewChunk(ChunkHeader chunkHeader, ReadOnlyMemory<byte> transformHeader, int fileSize, CancellationToken token) {
		Ensure.NotNull(chunkHeader, "chunkHeader");
		Ensure.Positive(fileSize, "fileSize");

		TFChunk.TFChunk chunk;
		bool triggerCaching;
		await _chunksLocker.AcquireAsync(token);
		try {
			if (chunkHeader.ChunkStartNumber != _chunksCount)
				throw new Exception(string.Format(
					"Received request to create a new ongoing chunk #{0}-{1}, but current chunks count is {2}.",
					chunkHeader.ChunkStartNumber, chunkHeader.ChunkEndNumber, _chunksCount));

			var chunkName = FileSystem.LocalNamingStrategy.GetFilenameFor(chunkHeader.ChunkStartNumber, 0);
			chunk = await TFChunk.TFChunk.CreateWithHeader(
				FileSystem,
				chunkName,
				chunkHeader,
				fileSize,
				_config.InMemDb,
				unbuffered: _config.Unbuffered,
				writethrough: _config.WriteThrough,
				reduceFileCachePressure: _config.ReduceFileCachePressure,
				tracker: _tracker,
				transformFactory: _transformManager.GetFactoryForExistingChunk(chunkHeader.TransformType),
				getTransformFactory: _transformManager,
				transformHeader: transformHeader,
				token);
			AddChunk(chunk);
			triggerCaching = _cachingEnabled;
		} finally {
			_chunksLocker.Release();
		}

		// trigger caching out of lock to avoid lock contention
		if (triggerCaching)
			TriggerBackgroundCaching();
		return chunk;
	}

	private void AddChunk(TFChunk.TFChunk chunk) {
		Debug.Assert(chunk is not null);
		Debug.Assert(_chunksLocker.IsLockHeld);

		var info = chunk.ChunkInfo;
		for (int i = info.ChunkStartNumber; i <= info.ChunkEndNumber; ++i) {
			_chunks[i] = chunk;
		}

		_chunksCount = Math.Max(info.ChunkEndNumber + 1, _chunksCount);
	}

	public async ValueTask AddChunk(TFChunk.TFChunk chunk, CancellationToken token) {
		Ensure.NotNull(chunk, "chunk");

		bool triggerCaching;
		await _chunksLocker.AcquireAsync(token);
		try {
			AddChunk(chunk);
			triggerCaching = _cachingEnabled;
		} finally {
			_chunksLocker.Release();
		}

		// trigger caching out of lock to avoid lock contention
		if (triggerCaching)
			TriggerBackgroundCaching();
	}

	// Takes a collection of locators for chunks which must be contiguous.
	// Atomically switches them in if the range precisely overlaps one or more chunks in _chunks
	public async ValueTask<bool> SwitchInCompletedChunks(IReadOnlyList<string> locators, CancellationToken token) {
		var newChunks = new TFChunk.TFChunk[locators.Count];
		var i = 0;
		try {
			for (; i < locators.Count; i++) {
				var chunk = await TFChunk.TFChunk.FromCompletedFile(
					fileSystem: FileSystem,
					filename: locators[i],
					verifyHash: false,
					unbufferedRead: _config.Unbuffered,
					tracker: _tracker,
					getTransformFactory: _transformManager,
					reduceFileCachePressure: _config.ReduceFileCachePressure,
					token: token);

				newChunks[i] = chunk;
			}
		} catch {
			for (var j = 0; j < i; j++) {
				newChunks[i].Dispose();
			}

			throw;
		}

		return await SwitchInChunks(newChunks, removeChunksAfter: null, token);
	}

	// Converts the specified temp chunk to permanent, and switches it in.
	public async ValueTask<TFChunk.TFChunk> SwitchInTempChunk(TFChunk.TFChunk chunk, bool verifyHash,
		bool removeChunksWithGreaterNumbers,
		CancellationToken token) {
		Ensure.NotNull(chunk, "chunk");

		var chunkHeader = chunk.ChunkHeader;

		// convert to new, permanent chunk
		var newChunk = await MakeTempChunkPermanent(chunk, verifyHash, token);

		// switch the new chunk into the chunks array.
		int? removeChunksAfter = removeChunksWithGreaterNumbers
			? chunkHeader.ChunkEndNumber // only true during replication
			: null;
		await SwitchInChunks([newChunk], removeChunksAfter, token);
		return newChunk;
	}

	// The specified chunk is temporary, but complete. It needs closing and renaming.
	private async ValueTask<TFChunk.TFChunk> MakeTempChunkPermanent(
		TFChunk.TFChunk chunk,
		bool verifyHash,
		CancellationToken token) {

		Ensure.NotNull(chunk, "chunk");

		if (!chunk.IsReadOnly)
			throw new ArgumentException(string.Format("Passed TFChunk is not completed: {0}.", chunk.ChunkLocator));

		var chunkHeader = chunk.ChunkHeader;
		var oldFileName = chunk.LocalFileName;

		Log.Information("Switching chunk #{chunkStartNumber}-{chunkEndNumber} ({oldFileName})...",
			chunkHeader.ChunkStartNumber, chunkHeader.ChunkEndNumber, Path.GetFileName(oldFileName));
		TFChunk.TFChunk newChunk;

		if (_config.InMemDb)
			newChunk = chunk;
		else {
			chunk.Dispose();
			try {
				chunk.WaitForDestroy(0); // should happen immediately
			} catch (TimeoutException exc) {
				throw new Exception(
					string.Format("The chunk that is being switched {0} is used by someone else.", chunk), exc);
			}

			// calculate the appropriate chunk version and move the chunk to the right filename.
			var newFileName =
				FileSystem.LocalNamingStrategy.DetermineNewVersionFilenameForIndex(chunkHeader.ChunkStartNumber, defaultVersion: 1);
			Log.Information("File {oldFileName} will be moved to file {newFileName}", Path.GetFileName(oldFileName),
				Path.GetFileName(newFileName));
			try {
				File.Move(oldFileName, newFileName);
			} catch (IOException) {
				WindowsProcessUtil.PrintWhoIsLocking(oldFileName, Log);
				WindowsProcessUtil.PrintWhoIsLocking(newFileName, Log);
				throw;
			}

			newChunk = await TFChunk.TFChunk.FromCompletedFile(FileSystem, newFileName, verifyHash, _config.Unbuffered,
				_tracker, _transformManager,
				_config.ReduceFileCachePressure, token: token);
		}

		return newChunk;
	}

	// Takes a collection of newChunks which must be contiguous.
	// Atomically switches them in if the range precisely overlaps one or more chunks in _chunks
	private async ValueTask<bool> SwitchInChunks(
		IReadOnlyList<TFChunk.TFChunk> newChunks,
		int? removeChunksAfter,
		CancellationToken token) {

		Ensure.NotNull(newChunks, nameof(newChunks));
		var ret = true;

		bool triggerCaching;
		await _chunksLocker.AcquireAsync(token);
		try {
			if (ReplaceChunksWith(newChunks, "Old")) {
				foreach (var newChunk in newChunks) {
					OnChunkSwitched?.Invoke(newChunk.ChunkInfo);
				}
			} else {
				foreach (var newChunk in newChunks) {
					Log.Information("Chunk {chunk} will be not switched, marking for remove...", newChunk);
					newChunk.MarkForDeletion();
				}
				// there was never any provision for early return here, but maybe there should be.
				// it only depends on the removeChunksAfter is not null case (replication)
				ret = false;
			}

			// only true during replication
			if (removeChunksAfter.HasValue) {
				var oldChunksCount = _chunksCount;
				_chunksCount = newChunks[^1].ChunkHeader.ChunkEndNumber + 1;
				RemoveChunks(removeChunksAfter.Value + 1, oldChunksCount - 1, "Excessive");
				if (_chunks[_chunksCount] is not null)
					throw new Exception(string.Format("Excessive chunk #{0} found after raw replication switch.",
						_chunksCount));
			}
			triggerCaching = _cachingEnabled;
		} finally {
			_chunksLocker.Release();
		}

		// trigger caching out of lock to avoid lock contention
		if (triggerCaching)
			TriggerBackgroundCaching();

		return ret;
	}

	// Checks this chunk has a compatible range to be swapped in and swaps it in.
	// Returns false if the range is not compatible. (This would be unexpected?)
	private bool ReplaceChunksWith(IReadOnlyList<TFChunk.TFChunk> newChunks, string chunkExplanation) {
		Debug.Assert(_chunksLocker.IsLockHeld);

		if (newChunks.Count is 0)
			return true;

		var chunkStartNumber = newChunks[0].ChunkHeader.ChunkStartNumber;
		var chunkEndNumber = newChunks[^1].ChunkHeader.ChunkEndNumber;

		// check newChunks are contiguous
		var expectedStart = chunkStartNumber;
		foreach (var newChunk in newChunks) {
			if (newChunk.ChunkHeader.ChunkStartNumber != expectedStart) {
				Log.Error(
					"Cannot replace chunks because new chunks are not contiguous. " +
					"ExpectedChunkNumber {Expected}. ActualChunkNumber {Actual}",
					expectedStart, newChunk.ChunkHeader.ChunkStartNumber);
				return false;
			}
			expectedStart = newChunk.ChunkHeader.ChunkEndNumber + 1;
		}

		// check that the range covered by new chunks exactly covers one or more existing chunks
		for (int i = chunkStartNumber; i <= chunkEndNumber;) {
			var chunk = _chunks[i];
			if (chunk != null) {
				// we would be removing `chunk` and replacing it with `newChunks`.
				// check that chunk's range is covered by the newChunks.
				var chunkInfo = chunk.ChunkInfo;
				if (chunkInfo.ChunkStartNumber < chunkStartNumber || chunkInfo.ChunkEndNumber > chunkEndNumber)
					return false;
				i = chunkInfo.ChunkEndNumber + 1;
			} else {
				//Cover the case of initial replication of merged chunks where they were never set
				// in the map in the first place.
				i++;
			}
		}

		// switch the chunk in to _chunks array and mark any removed chunks for deletion.
		TFChunk.TFChunk previousRemovedChunk = null;
		var newChunkIndex = 0;
		for (int i = chunkStartNumber; i <= chunkEndNumber; i++) {
			while (!Covers(newChunks[newChunkIndex], chunkNumber: i))
				newChunkIndex++;
			// now newChunks[newChunkIndex] covers logical chunk i
			var oldChunk = Interlocked.Exchange(ref _chunks[i], newChunks[newChunkIndex]);
			if (!ReferenceEquals(previousRemovedChunk, oldChunk)) {
				// Once we've swapped all entries for the previousRemovedChunk we can safely delete it.
				if (previousRemovedChunk != null) {
					previousRemovedChunk.MarkForDeletion();
					Log.Information("{chunkExplanation} chunk #{oldChunk} is marked for deletion.", chunkExplanation,
						previousRemovedChunk);
				}

				previousRemovedChunk = oldChunk;
			}
		}

		if (previousRemovedChunk != null) {
			// Delete the last chunk swapped out now it's fully replaced.
			previousRemovedChunk.MarkForDeletion();
			Log.Information("{chunkExplanation} chunk #{oldChunk} is marked for deletion.", chunkExplanation,
				previousRemovedChunk);
		}

		return true;

		static bool Covers(TFChunk.TFChunk chunk, int chunkNumber) =>
			chunk.ChunkHeader.ChunkStartNumber <= chunkNumber &&
			chunk.ChunkHeader.ChunkEndNumber >= chunkNumber;
	}

	private void RemoveChunks(int chunkStartNumber, int chunkEndNumber, string chunkExplanation) {
		Debug.Assert(_chunksLocker.IsLockHeld);

		TFChunk.TFChunk lastRemovedChunk = null;
		for (int i = chunkStartNumber; i <= chunkEndNumber; i += 1) {
			var oldChunk = Interlocked.Exchange(ref _chunks[i], null);
			if (oldChunk != null && !ReferenceEquals(lastRemovedChunk, oldChunk)) {
				oldChunk.MarkForDeletion();
				Log.Information("{chunkExplanation} chunk {oldChunk} is marked for deletion.", chunkExplanation, oldChunk);
			}

			lastRemovedChunk = oldChunk;
		}
	}

	private void TriggerBackgroundCaching() {
		Interlocked.Increment(ref _backgroundPassesRemaining);
		if (Interlocked.CompareExchange(ref _backgroundRunning, 1, 0) == 0)
			ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
	}

	public ValueTask<TFChunk.TFChunk> GetInitializedChunkFor(long logPosition, CancellationToken token) {
		ValueTask<TFChunk.TFChunk> task;

		var chunkNum = (int)(logPosition / _config.ChunkSize);
		if ((uint)chunkNum >= (uint)_chunksCount) {
			task = ValueTask.FromException<TFChunk.TFChunk>(new ArgumentOutOfRangeException(nameof(logPosition),
				$"LogPosition {logPosition} does not have corresponding chunk in DB."));
		}
		else if (_chunks[chunkNum] is not { } chunk) {
			task = ValueTask.FromException<TFChunk.TFChunk>(new Exception(
				$"Requested chunk for LogPosition {logPosition}, which is not present in TFChunkManager."));
		}
		else if (chunk.Initialized) {
			task = new(chunk);
		} else {
			task = EnsureInitialized(chunk, token);
		}

		return task;
	}

	TFChunk.TFChunk IChunkRegistry<TFChunk.TFChunk>.UnsafeTryGetChunkFor(long logPosition) {
		var chunkNum = (int)(logPosition / _config.ChunkSize);
		return (uint)chunkNum < (uint)_chunksCount ? _chunks[chunkNum] : null;
	}

	public ValueTask<TFChunk.TFChunk> TryGetInitializedChunkFor(long logPosition, CancellationToken token) {
		var chunkNum = (int)(logPosition / _config.ChunkSize);

		ValueTask<TFChunk.TFChunk> task;
		if ((uint)chunkNum >= (uint)_chunksCount || _chunks[chunkNum] is not { } chunk) {
			task = default;
		} else if (chunk.Initialized) {
			task = new(chunk);
		} else {
			task = EnsureInitialized(chunk, token);
		}

		return task;
	}

	public TFChunk.TFChunk UnsafeGetChunk(int chunkNum) {
		if ((uint)chunkNum >= (uint)_chunksCount)
			throw new ArgumentOutOfRangeException(nameof(chunkNum),
				$"Chunk #{chunkNum} is not present in DB.");

		if (_chunks[chunkNum] is not { } chunk)
			throw new Exception($"Requested chunk #{chunkNum}, which is not present in TFChunkManager.");

		return chunk;
	}

	public ValueTask<TFChunk.TFChunk> GetInitializedChunk(int chunkNum, CancellationToken token) {
		ValueTask<TFChunk.TFChunk> task;
		if ((uint)chunkNum >= (uint)_chunksCount) {
			task = ValueTask.FromException<TFChunk.TFChunk>(new ArgumentOutOfRangeException(nameof(chunkNum),
				$"Chunk #{chunkNum} is not present in DB."));
		}
		else if (_chunks[chunkNum] is not { } chunk) {
			task = ValueTask.FromException<TFChunk.TFChunk>(new Exception(
				$"Requested chunk #{chunkNum}, which is not present in TFChunkManager."));
		}
		else if (chunk.Initialized) {
			task = new(chunk);
		} else {
			task = EnsureInitialized(chunk, token);
		}

		return task;
	}

	private static async ValueTask<TFChunk.TFChunk> EnsureInitialized(TFChunk.TFChunk chunk, CancellationToken token) {
		await chunk.EnsureInitialized(token);
		return chunk;
	}

	public async ValueTask<bool> TryClose(CancellationToken token) {
		var allChunksClosed = true;

		await _chunksLocker.AcquireAsync(token);
		try {
			for (int i = 0; i < _chunksCount; ++i) {
				if (_chunks[i] is { } chunk)
					allChunksClosed &= chunk.TryClose();
			}
		} finally {
			_chunksLocker.Release();
		}

		return allChunksClosed;
	}
}
