// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Diagnostics;
using EventStore.Common.Utils;
using EventStore.Core.Exceptions;
using EventStore.Core.TransactionLog;
using EventStore.Core.Util;
using EventStore.Core.Index.Hashes;
using EventStore.Core.Settings;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using ILogger = Serilog.ILogger;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.LogCommon;

namespace EventStore.Core.Index;

public abstract class TableIndex {
	internal static readonly IndexEntry InvalidIndexEntry = new IndexEntry(0, -1, -1);
	public const string IndexMapFilename = "indexmap";
	public const string ForceIndexVerifyFilename = ".forceverify";
	protected static readonly ILogger Log = Serilog.Log.ForContext<TableIndex>();
}

public class TableIndex<TStreamId> : TableIndex, ITableIndex<TStreamId> {
	private const int MaxMemoryTables = 1;

	public long CommitCheckpoint {
		get { return Interlocked.Read(ref _commitCheckpoint); }
	}

	public long PrepareCheckpoint {
		get { return Interlocked.Read(ref _prepareCheckpoint); }
	}

	private readonly int _maxSizeForMemory;
	private readonly int _maxTablesPerLevel;
	private readonly bool _additionalReclaim;
	private readonly bool _inMem;
	private readonly bool _skipIndexVerify;
	private readonly int _indexCacheDepth;
	private readonly bool _useBloomFilter;
	private readonly int _lruCacheSize;
	private readonly int _initializationThreads;
	private readonly byte _ptableVersion;
	private readonly string _directory;
	private readonly Func<IMemTable> _memTableFactory;
	private readonly Func<TFReaderLease> _tfReaderFactory;
	private readonly IIndexFilenameProvider _fileNameProvider;
	private readonly IIndexStatusTracker _statusTracker;

	private readonly object _awaitingTablesLock = new object();

	private IndexMap _indexMap;
	private List<TableItem> _awaitingMemTables;
	private volatile bool _isManualMergePending;

	private long _commitCheckpoint = -1;
	private long _prepareCheckpoint = -1;

	// there are two background processes, scavenge and writing/merging ptables.
	private volatile bool _backgroundRunning;
	private readonly ManualResetEventSlim _backgroundRunningEvent = new ManualResetEventSlim(true);

	private IHasher<TStreamId> _lowHasher;
	private IHasher<TStreamId> _highHasher;
	private readonly TStreamId _emptyStreamId;

	private bool _initialized;
	private readonly int _maxAutoMergeIndexLevel;
	private readonly int _pTableMaxReaderCount;

	public TableIndex(string directory,
		IHasher<TStreamId> lowHasher,
		IHasher<TStreamId> highHasher,
		TStreamId emptyStreamId,
		Func<IMemTable> memTableFactory,
		Func<TFReaderLease> tfReaderFactory,
		byte ptableVersion,
		int maxAutoMergeIndexLevel,
		int pTableMaxReaderCount,
		int maxSizeForMemory = 1000000,
		int maxTablesPerLevel = 4,
		bool additionalReclaim = false,
		bool inMem = false,
		bool skipIndexVerify = false,
		int indexCacheDepth = 16,
		int initializationThreads = 1,
		bool useBloomFilter = true,
		int lruCacheSize = 1_000_000,
		IIndexStatusTracker statusTracker = null) {

		Ensure.NotNullOrEmpty(directory, "directory");
		Ensure.NotNull(memTableFactory, "memTableFactory");
		Ensure.NotNull(lowHasher, "lowHasher");
		Ensure.NotNull(highHasher, "highHasher");
		Ensure.NotNull(tfReaderFactory, "tfReaderFactory");
		Ensure.Positive(initializationThreads, "initializationThreads");
		Ensure.Positive(pTableMaxReaderCount, "pTableMaxReaderCount");

		if (maxTablesPerLevel <= 1)
			throw new ArgumentOutOfRangeException("maxTablesPerLevel");

		if (indexCacheDepth > 28 || indexCacheDepth < 8) throw new ArgumentOutOfRangeException("indexCacheDepth");

		_directory = directory;
		_memTableFactory = memTableFactory;
		_tfReaderFactory = tfReaderFactory;
		_fileNameProvider = new GuidFilenameProvider(directory);
		_statusTracker = statusTracker ?? new IndexStatusTracker.NoOp();
		_maxSizeForMemory = maxSizeForMemory;
		_maxTablesPerLevel = maxTablesPerLevel;
		_additionalReclaim = additionalReclaim;
		_inMem = inMem;
		_skipIndexVerify = ShouldForceIndexVerify() ? false : skipIndexVerify;
		_indexCacheDepth = indexCacheDepth;
		_initializationThreads = initializationThreads;
		_useBloomFilter = useBloomFilter;
		_lruCacheSize = lruCacheSize;
		_ptableVersion = ptableVersion;
		_awaitingMemTables = new List<TableItem> {new TableItem(_memTableFactory(), -1, -1, 0)};

		_lowHasher = lowHasher;
		_highHasher = highHasher;
		_emptyStreamId = emptyStreamId;

		_maxAutoMergeIndexLevel = maxAutoMergeIndexLevel;
		_pTableMaxReaderCount = pTableMaxReaderCount;
	}

	public void Initialize(long chaserCheckpoint) {
		Ensure.Nonnegative(chaserCheckpoint, "chaserCheckpoint");

		//NOT THREAD SAFE (assumes one thread)
		if (_initialized)
			throw new IOException("TableIndex is already initialized.");
		_initialized = true;

		if (_inMem) {
			_indexMap = IndexMap.CreateEmpty(_maxTablesPerLevel, int.MaxValue, _pTableMaxReaderCount);
			_prepareCheckpoint = _indexMap.PrepareCheckpoint;
			_commitCheckpoint = _indexMap.CommitCheckpoint;
			return;
		}

		if (ShouldForceIndexVerify()) {
			Log.Debug("Forcing verification of index files...");
		}

		CreateIfDoesNotExist(_directory);
		var indexmapFile = Path.Combine(_directory, IndexMapFilename);

		// if TableIndex's CommitCheckpoint is >= amount of written TFChunk data,
		// we'll have to remove some of PTables as they point to non-existent data
		// this can happen (very unlikely, though) on leader crash
		try {
			_indexMap = IndexMap.FromFile(indexmapFile, _maxTablesPerLevel, true, _indexCacheDepth,
				_skipIndexVerify,
				useBloomFilter: _useBloomFilter,
				lruCacheSize: _lruCacheSize,
				threads: _initializationThreads,
				maxAutoMergeLevel: _maxAutoMergeIndexLevel,
				pTableMaxReaderCount: _pTableMaxReaderCount);
			if (_indexMap.CommitCheckpoint >= chaserCheckpoint) {
				_indexMap.Dispose(TimeSpan.FromMilliseconds(5000));
				throw new CorruptIndexException(String.Format(
					"IndexMap's CommitCheckpoint ({0}) is greater than ChaserCheckpoint ({1}).",
					_indexMap.CommitCheckpoint, chaserCheckpoint));
			}

			//verification should be completed by now
			DeleteForceIndexVerifyFile();
		} catch (CorruptIndexException exc) {
			Log.Error(exc, "ReadIndex is corrupted...");
			LogIndexMapContent(indexmapFile);
			DumpAndCopyIndex();
			File.SetAttributes(indexmapFile, FileAttributes.Normal);
			File.Delete(indexmapFile);
			DeleteForceIndexVerifyFile();
			_indexMap = IndexMap.FromFile(indexmapFile, _maxTablesPerLevel, true, _indexCacheDepth,
				_skipIndexVerify,
				useBloomFilter: _useBloomFilter,
				lruCacheSize: _lruCacheSize,
				threads: _initializationThreads,
				maxAutoMergeLevel: _maxAutoMergeIndexLevel,
				pTableMaxReaderCount: _pTableMaxReaderCount);
		}

		_prepareCheckpoint = _indexMap.PrepareCheckpoint;
		_commitCheckpoint = _indexMap.CommitCheckpoint;

		// clean up all other remaining files
		var indexFiles = _indexMap.InOrder().SelectMany(x => new[] {
				Path.GetFileName(x.Filename),
 					Path.GetFileName(x.BloomFilterFilename) })
			.Union(new[] {IndexMapFilename});
		var toDeleteFiles = Directory.EnumerateFiles(_directory).Select(Path.GetFileName)
			.Except(indexFiles, StringComparer.OrdinalIgnoreCase);
		foreach (var filePath in toDeleteFiles) {
			var file = Path.Combine(_directory, filePath);
			File.SetAttributes(file, FileAttributes.Normal);
			File.Delete(file);
		}
	}

	private static void LogIndexMapContent(string indexmapFile) {
		try {
			Log.Error("IndexMap '{indexMap}' content:\n {content}", indexmapFile,
				Helper.FormatBinaryDump(File.ReadAllBytes(indexmapFile)));
		} catch (Exception exc) {
			Log.Error(exc, "Unexpected error while dumping IndexMap '{indexMap}'.", indexmapFile);
		}
	}

	private void DumpAndCopyIndex() {
		string dumpPath = null;
		try {
			dumpPath = Path.Combine(Path.GetDirectoryName(_directory),
				string.Format("index-backup-{0:yyyy-MM-dd_HH-mm-ss.fff}", DateTime.UtcNow));
			Log.Error("Making backup of index folder for inspection to {dumpPath}...", dumpPath);
			FileUtils.DirectoryCopy(_directory, dumpPath, copySubDirs: true);
		} catch (Exception exc) {
			Log.Error(exc, "Unexpected error while copying index to backup dir '{dumpPath}'", dumpPath);
		}
	}

	private static void CreateIfDoesNotExist(string directory) {
		if (!Directory.Exists(directory))
			Directory.CreateDirectory(directory);
	}

	public void Add(long commitPos, TStreamId streamId, long version, long position) {
		Ensure.Nonnegative(commitPos, "commitPos");
		Ensure.Nonnegative(version, "version");
		Ensure.Nonnegative(position, "position");

		AddEntries(commitPos, new[] {CreateIndexKey(streamId, version, position)});
	}

	public void AddEntries(long commitPos, IList<IndexKey<TStreamId>> entries) {
		//should only be called on a single thread.
		var table = (IMemTable)_awaitingMemTables[0].Table; // always a memtable

		var collection = entries.Select(x => CreateIndexEntry(x)).ToList();
		table.AddEntries(collection);

		if (table.Count >= _maxSizeForMemory) {
			long prepareCheckpoint = collection[0].Position;
			for (int i = 1, n = collection.Count; i < n; ++i) {
				prepareCheckpoint = Math.Max(prepareCheckpoint, collection[i].Position);
			}

			TryProcessAwaitingTables(commitPos, prepareCheckpoint);
		}
	}

	public Task MergeIndexes() {
		TryManualMerge();
		return Task.CompletedTask;
	}

	public bool IsBackgroundTaskRunning {
		get { return _backgroundRunning; }
	}

	//Automerge only
	private void TryProcessAwaitingTables(long commitPos, long prepareCheckpoint) {
		lock (_awaitingTablesLock) {
			var newTables = new List<TableItem> {new TableItem(_memTableFactory(), -1, -1, 0)};
			newTables.AddRange(_awaitingMemTables.Select(
				(x, i) => i == 0 ? new TableItem(x.Table, prepareCheckpoint, commitPos, x.Level) : x));

			Log.Debug("Switching MemTable, currently: {awaitingMemTables} awaiting tables.", newTables.Count);

			_awaitingMemTables = newTables;
			if (_inMem) return;
			TryProcessAwaitingTables();

			if (_additionalReclaim)
				ThreadPool.QueueUserWorkItem(x => ReclaimMemoryIfNeeded(_awaitingMemTables));
		}
	}

	public void TryManualMerge() {
		_isManualMergePending = true;
		TryProcessAwaitingTables();
	}

	private void TryProcessAwaitingTables() {
		lock (_awaitingTablesLock) {
			if (!_backgroundRunning) {
				_backgroundRunningEvent.Reset();
				_backgroundRunning = true;
				ThreadPool.QueueUserWorkItem(x => ReadOffQueue());
			}
		}
	}

	private void ReadOffQueue() {
		try {
			using var _ = _statusTracker.StartMerging();
			while (true) {
				var indexmapFile = Path.Combine(_directory, IndexMapFilename);

				if (_isManualMergePending) {
					Log.Debug("Performing manual index merge.");

					_isManualMergePending = false;
					var manualMergeResult = _indexMap.TryManualMerge(
						_fileNameProvider,
						_ptableVersion,
						_indexCacheDepth,
						_skipIndexVerify,
						useBloomFilter: _useBloomFilter,
						lruCacheSize: _lruCacheSize);

					if (manualMergeResult.HasMergedAny) {
						_indexMap = manualMergeResult.MergedMap;
						_indexMap.SaveToFile(indexmapFile);
						manualMergeResult.ToDelete.ForEach(x => x.MarkForDestruction());
					}
					Log.Debug("Manual index merge completed: {numMergedPTables} PTable(s) merged.", manualMergeResult.ToDelete.Count);
				}

				TableItem tableItem;
				//ISearchTable table;
				lock (_awaitingTablesLock) {
					Log.Debug("Awaiting tables queue size is: {awaitingMemTables}.", _awaitingMemTables.Count);
					if (_awaitingMemTables.Count == 1) {
						return;
					}

					tableItem = _awaitingMemTables[_awaitingMemTables.Count - 1];
				}

				PTable ptable;
				var memtable = tableItem.Table as IMemTable;
				if (memtable != null) {
					memtable.MarkForConversion();
					ptable = PTable.FromMemtable(memtable, _fileNameProvider.GetFilenameNewTable(),
						ESConsts.PTableInitialReaderCount,
						_pTableMaxReaderCount,
						_indexCacheDepth,
						_skipIndexVerify,
						useBloomFilter: _useBloomFilter,
						lruCacheSize: _lruCacheSize);
				} else
					ptable = (PTable)tableItem.Table;

				var addResult = _indexMap.AddPTable(ptable, tableItem.PrepareCheckpoint, tableItem.CommitCheckpoint);
				_indexMap = addResult.NewMap;
				_indexMap.SaveToFile(indexmapFile);

				if (addResult.CanMergeAny) {
					MergeResult mergeResult;
					do {
						mergeResult = _indexMap.TryMergeOneLevel(
							_fileNameProvider,
							_ptableVersion,
							_indexCacheDepth,
							_skipIndexVerify,
							useBloomFilter: _useBloomFilter,
							lruCacheSize: _lruCacheSize);

						if (mergeResult.HasMergedAny) {
							_indexMap = mergeResult.MergedMap;
							_indexMap.SaveToFile(indexmapFile);
							mergeResult.ToDelete.ForEach(x => x.MarkForDestruction());
						}
					} while (mergeResult.CanMergeAny);
				}

				lock (_awaitingTablesLock) {
					var memTables = _awaitingMemTables.ToList();

					var corrTable = memTables.First(x => x.Table.Id == ptable.Id);
					memTables.Remove(corrTable);

					// parallel thread could already switch table,
					// so if we have another PTable instance with same ID,
					// we need to kill that instance as we added ours already
					if (!ReferenceEquals(corrTable.Table, ptable) && corrTable.Table is PTable)
						((PTable)corrTable.Table).MarkForDestruction();

					Log.Debug("There are now {awaitingMemTables} awaiting tables.", memTables.Count);
					_awaitingMemTables = memTables;
				}
			}
		} catch (FileBeingDeletedException exc) {
			Log.Error(exc,
				"Could not acquire chunk in TableIndex.ReadOffQueue. It is OK if node is shutting down.");
		} catch (Exception exc) {
			Log.Error(exc, "Error in TableIndex.ReadOffQueue");
			throw;
		} finally {
			lock (_awaitingTablesLock) {
				_backgroundRunning = false;
				_backgroundRunningEvent.Set();
			}
		}
	}

	public void WaitForBackgroundTasks(int millisecondsTimeout = 7_000) {
		if (!_backgroundRunningEvent.Wait(millisecondsTimeout)) {
			throw new TimeoutException(
				$"Waiting for TableIndex background tasks took longer than {millisecondsTimeout:N0} ms.");
		}
	}

	public ValueTask Scavenge(IIndexScavengerLog log, CancellationToken ct) =>
		Scavenge(shouldKeep: null, log: log, ct: ct);

	public async ValueTask Scavenge(
		Func<IndexEntry, CancellationToken, ValueTask<bool>> shouldKeep,
		IIndexScavengerLog log,
		CancellationToken ct) {

		GetExclusiveBackgroundTask(ct);
		var sw = Stopwatch.StartNew();

		try {
			using var _ = _statusTracker.StartScavenging();
			Log.Information("Starting scavenge of TableIndex.");
			await ScavengeInternal(shouldKeep, log, ct);
		} finally {
			lock (_awaitingTablesLock) {
				_backgroundRunning = false;
				_backgroundRunningEvent.Set();

				TryProcessAwaitingTables();
			}

			Log.Information("Completed scavenge of TableIndex.  Elapsed: {elapsed}", sw.Elapsed);
		}
	}

	private async ValueTask ScavengeInternal(
		Func<IndexEntry, CancellationToken, ValueTask<bool>> shouldKeep,
		IIndexScavengerLog log,
		CancellationToken ct) {

		var toScavenge = _indexMap.InOrder().ToList();

		foreach (var pTable in toScavenge) {
			var startNew = new Timestamp();

			try {
				using (var reader = _tfReaderFactory()) {
					var indexmapFile = Path.Combine(_directory, IndexMapFilename);

					Func<IndexEntry, CancellationToken, ValueTask<bool>> existsAt =
						(entry, token) => reader.ExistsAt(entry.Position, token);

					var scavengeResult = await _indexMap.Scavenge(
						pTable.Id,
						ct,
						shouldKeep ?? existsAt,
						_fileNameProvider,
						_ptableVersion,
						_indexCacheDepth,
						_skipIndexVerify,
						useBloomFilter: _useBloomFilter,
						lruCacheSize: _lruCacheSize);

					if (scavengeResult.IsSuccess) {
						_indexMap = scavengeResult.ScavengedMap;
						_indexMap.SaveToFile(indexmapFile);

						scavengeResult.OldTable.MarkForDestruction();

						var entriesDeleted = scavengeResult.OldTable.Count - scavengeResult.NewTable.Count;
						log.IndexTableScavenged(scavengeResult.Level, scavengeResult.Index, startNew.Elapsed,
							entriesDeleted, scavengeResult.NewTable.Count, scavengeResult.SpaceSaved);
					} else {
						log.IndexTableNotScavenged(scavengeResult.Level, scavengeResult.Index, startNew.Elapsed,
							pTable.Count, "");
					}
				}
			} catch (OperationCanceledException) {
				log.IndexTableNotScavenged(-1, -1, startNew.Elapsed, pTable.Count, "Scavenge cancelled");
				throw;
			} catch (Exception ex) {
				log.IndexTableNotScavenged(-1, -1, startNew.Elapsed, pTable.Count, ex.Message);
				throw;
			}
		}
	}

	private void GetExclusiveBackgroundTask(CancellationToken ct) {
		while (true) {
			lock (_awaitingTablesLock) {
				if (!_backgroundRunning) {
					_backgroundRunningEvent.Reset();
					_backgroundRunning = true;
					return;
				}
			}

			Log.Information("Waiting for TableIndex background task to complete before starting scavenge.");
			_backgroundRunningEvent.Wait(ct);
		}
	}

	private void ReclaimMemoryIfNeeded(List<TableItem> awaitingMemTables) {
		var toPutOnDisk = awaitingMemTables.OfType<IMemTable>().Count() - MaxMemoryTables;
		for (var i = awaitingMemTables.Count - 1; i >= 1 && toPutOnDisk > 0; i--) {
			var memtable = awaitingMemTables[i].Table as IMemTable;
			if (memtable == null || !memtable.MarkForConversion())
				continue;

			Log.Debug("Putting awaiting file as PTable instead of MemTable [{id}].", memtable.Id);

			var ptable = PTable.FromMemtable(memtable, _fileNameProvider.GetFilenameNewTable(),
				ESConsts.PTableInitialReaderCount,
				_pTableMaxReaderCount,
				_indexCacheDepth,
				_skipIndexVerify,
				useBloomFilter: _useBloomFilter,
				lruCacheSize: _lruCacheSize);
			var swapped = false;
			lock (_awaitingTablesLock) {
				for (var j = _awaitingMemTables.Count - 1; j >= 1; j--) {
					var tableItem = _awaitingMemTables[j];
					if (!(tableItem.Table is IMemTable) || tableItem.Table.Id != ptable.Id) continue;
					swapped = true;
					_awaitingMemTables[j] = new TableItem(ptable,
						tableItem.PrepareCheckpoint,
						tableItem.CommitCheckpoint,
						tableItem.Level);
					break;
				}
			}

			if (!swapped)
				ptable.MarkForDestruction();
			toPutOnDisk--;
		}
	}

	public void Visit(Action<PTable> f) {
		int counter = 0;
		while (counter < 5) {
			counter++;
			try {
				VisitInternal(f);
				return;
			} catch (FileBeingDeletedException) {
				Log.Debug("File being deleted.");
			} catch (MaybeCorruptIndexException) {
				ForceIndexVerifyOnNextStartup();
				throw;
			}
		}

		throw new InvalidOperationException("Files are locked.");
	}

	private void VisitInternal(Action<PTable> f) {
		var map = _indexMap;
		foreach (var table in map.InOrder()) {
			f(table);
		}
	}

	public bool TryGetOneValue(TStreamId streamId, long version, out long position) {
		ulong stream = CreateHash(streamId);
		int counter = 0;
		while (counter < 5) {
			counter++;
			try {
				return TryGetOneValueInternal(stream, version, out position);
			} catch (FileBeingDeletedException) {
				Log.Debug("File being deleted.");
			} catch (MaybeCorruptIndexException) {
				ForceIndexVerifyOnNextStartup();
				throw;
			}
		}

		throw new InvalidOperationException("Files are locked.");
	}

	private bool TryGetOneValueInternal(ulong stream, long version, out long position) {
		if (version < 0)
			throw new ArgumentOutOfRangeException("version");

		var awaiting = _awaitingMemTables;
		foreach (var tableItem in awaiting) {
			if (tableItem.Table.TryGetOneValue(stream, version, out position))
				return true;
		}

		var map = _indexMap;
		foreach (var table in map.InOrder()) {
			if (table.TryGetOneValue(stream, version, out position))
				return true;
		}

		position = 0;
		return false;
	}

	public bool TryGetLatestEntry(TStreamId streamId, out IndexEntry entry) {
		ulong stream = CreateHash(streamId);
		var counter = 0;
		while (counter < 5) {
			counter++;
			try {
				return TryGetLatestEntryInternal(stream, out entry);
			} catch (FileBeingDeletedException) {
				Log.Debug("File being deleted.");
			} catch (MaybeCorruptIndexException) {
				ForceIndexVerifyOnNextStartup();
				throw;
			}
		}

		throw new InvalidOperationException("Files are locked.");
	}

	private bool TryGetLatestEntryInternal(ulong stream, out IndexEntry entry) {
		var awaiting = _awaitingMemTables;
		foreach (var t in awaiting) {
			if (t.Table.TryGetLatestEntry(stream, out entry))
				return true;
		}

		var map = _indexMap;
		foreach (var table in map.InOrder()) {
			if (table.TryGetLatestEntry(stream, out entry))
				return true;
		}

		entry = InvalidIndexEntry;
		return false;
	}

	public async ValueTask<IndexEntry?> TryGetLatestEntry(ulong stream, long beforePosition, Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream, CancellationToken token) {
		var counter = 0;
		while (counter < 5) {
			counter++;
			try {
				return await TryGetLatestEntryInternal(stream, beforePosition, isForThisStream, token);
			} catch (FileBeingDeletedException) {
				Log.Debug("File being deleted.");
			} catch (MaybeCorruptIndexException) {
				ForceIndexVerifyOnNextStartup();
				throw;
			}
		}

		throw new InvalidOperationException("Files are locked.");
	}

	public ValueTask<IndexEntry?> TryGetLatestEntry(TStreamId streamId, long beforePosition, Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream, CancellationToken token) {
		ulong stream = CreateHash(streamId);
		return TryGetLatestEntry(stream, beforePosition, isForThisStream, token);
	}

	private async ValueTask<IndexEntry?> TryGetLatestEntryInternal(ulong stream, long beforePosition, Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream, CancellationToken token) {
		var awaiting = _awaitingMemTables;

		foreach (var t in awaiting) {
			if (await t.Table.TryGetLatestEntry(stream, beforePosition, isForThisStream, token) is { } entry)
				return entry;
		}

		var map = _indexMap;
		foreach (var table in map.InOrder()) {
			if (await table.TryGetLatestEntry(stream, beforePosition, isForThisStream, token) is { } entry)
				return entry;
		}

		return null;
	}

	public bool TryGetOldestEntry(TStreamId streamId, out IndexEntry entry) {
		ulong stream = CreateHash(streamId);
		var counter = 0;
		while (counter < 5) {
			counter++;
			try {
				return TryGetOldestEntryInternal(stream, out entry);
			} catch (FileBeingDeletedException) {
				Log.Debug("File being deleted.");
			} catch (MaybeCorruptIndexException) {
				ForceIndexVerifyOnNextStartup();
				throw;
			}
		}

		throw new InvalidOperationException("Files are locked.");
	}

	private bool TryGetOldestEntryInternal(ulong stream, out IndexEntry entry) {
		var map = _indexMap;
		foreach (var table in map.InReverseOrder()) {
			if (table.TryGetOldestEntry(stream, out entry))
				return true;
		}

		var awaiting = _awaitingMemTables;
		for (var index = awaiting.Count - 1; index >= 0; index--) {
			if (awaiting[index].Table.TryGetOldestEntry(stream, out entry))
				return true;
		}

		entry = InvalidIndexEntry;
		return false;
	}

	public bool TryGetNextEntry(TStreamId streamId, long afterVersion, out IndexEntry entry) {
		ulong stream = CreateHash(streamId);
		return TryGetNextEntry(stream, afterVersion, out entry);
	}

	public bool TryGetNextEntry(ulong stream, long afterVersion, out IndexEntry entry) {
		var counter = 0;
		while (counter < 5) {
			counter++;
			try {
				return TryGetNextEntryInternal(stream, afterVersion, out entry);
			} catch (FileBeingDeletedException) {
				Log.Debug("File being deleted.");
			} catch (MaybeCorruptIndexException) {
				ForceIndexVerifyOnNextStartup();
				throw;
			}
		}

		throw new InvalidOperationException("Files are locked.");
	}

	private bool TryGetNextEntryInternal(ulong stream, long afterVersion, out IndexEntry entry) {
		var map = _indexMap;
		foreach (var table in map.InReverseOrder()) {
			if (table.TryGetNextEntry(stream, afterVersion, out entry))
				return true;
		}

		var awaiting = _awaitingMemTables;
		for (var index = awaiting.Count - 1; index >= 0; index--) {
			if (awaiting[index].Table.TryGetNextEntry(stream, afterVersion, out entry))
				return true;
		}

		entry = InvalidIndexEntry;
		return false;
	}

	public bool TryGetPreviousEntry(TStreamId streamId, long beforeVersion, out IndexEntry entry) {
		ulong stream = CreateHash(streamId);
		return TryGetPreviousEntry(stream, beforeVersion, out entry);
	}

	public bool TryGetPreviousEntry(ulong stream, long beforeVersion, out IndexEntry entry) {
		var counter = 0;
		while (counter < 5) {
			counter++;
			try {
				return TryGetPreviousEntryInternal(stream, beforeVersion, out entry);
			} catch (FileBeingDeletedException) {
				Log.Debug("File being deleted.");
			} catch (MaybeCorruptIndexException) {
				ForceIndexVerifyOnNextStartup();
				throw;
			}
		}

		throw new InvalidOperationException("Files are locked.");
	}

	private bool TryGetPreviousEntryInternal(ulong stream, long beforeVersion, out IndexEntry entry) {
		var awaiting = _awaitingMemTables;

		foreach (var t in awaiting) {
			if (t.Table.TryGetPreviousEntry(stream, beforeVersion, out entry))
				return true;
		}

		var map = _indexMap;
		foreach (var table in map.InOrder()) {
			if (table.TryGetPreviousEntry(stream, beforeVersion, out entry))
				return true;
		}

		entry = InvalidIndexEntry;
		return false;
	}

	public IReadOnlyList<IndexEntry> GetRange(TStreamId streamId, long startVersion, long endVersion,
		int? limit = null) => GetRange(CreateHash(streamId), startVersion, endVersion, limit);

	public IReadOnlyList<IndexEntry> GetRange(ulong stream, long startVersion, long endVersion, int? limit = null) {
		var counter = 0;
		while (counter < 5) {
			counter++;
			try {
				return GetRangeInternal(stream, startVersion, endVersion, limit);
			} catch (FileBeingDeletedException) {
				Log.Debug("File being deleted.");
			} catch (MaybeCorruptIndexException) {
				ForceIndexVerifyOnNextStartup();
				throw;
			}
		}

		throw new InvalidOperationException("Files are locked.");
	}

	private IReadOnlyList<IndexEntry> GetRangeInternal(ulong hash, long startVersion, long endVersion,
		int? limit = null) {
		if (startVersion < 0)
			throw new ArgumentOutOfRangeException("startVersion");
		if (endVersion < 0)
			throw new ArgumentOutOfRangeException("endVersion");

		// 1. assemble results per table for memtables and ptables
		// discard any results with 0 entries.
		var resultsPerTable = new List<IReadOnlyList<IndexEntry>>(16);

		var awaiting = _awaitingMemTables;
		for (int index = 0; index < awaiting.Count; index++) {
			var range = awaiting[index].Table.GetRange(hash, startVersion, endVersion, limit);
			if (range.Count > 0)
				resultsPerTable.Add(range);
		}

		var map = _indexMap;
		foreach (var table in map.InOrder()) {
			var range = table.GetRange(hash, startVersion, endVersion, limit);
			if (range.Count > 0)
				resultsPerTable.Add(range);
		}

		// 2. iterate through the per table results producing candidate enumerators
		// if the results per table dont overlap then we can merge them much more quickly
		// because they're already in order. this will be so in the common case of
		// having no hash collisions.
		var maybeInOrder = true;
		var prevOldestVersion = long.MaxValue;
		var totalEntryCount = 0;
		var candidates = new List<IEnumerator<IndexEntry>>(resultsPerTable.Count);
		foreach (var entries in resultsPerTable) {
			var entriesEnumerator = entries.GetEnumerator();
			entriesEnumerator.MoveNext();
			candidates.Add(entriesEnumerator);

			totalEntryCount += entries.Count;

			if (maybeInOrder) {
				var latestVersion = entriesEnumerator.Current.Version;

				if (latestVersion >= prevOldestVersion) {
					maybeInOrder = false;
				}

				var oldestVersion = entries[^1].Version;
				prevOldestVersion = oldestVersion;
			}
		}
		var areInOrder = maybeInOrder;

		// 3. sort the entries by consuming the enumerators
		//    removes duplicates
		var last = new IndexEntry(0, 0, 0);
		var first = true;

		var sortedCandidates = new List<IndexEntry>(totalEntryCount);

		// reverse so that typically the largest candidate is at the back, making the RemoveAt cheap
		candidates.Reverse();

		while (candidates.Count > 0) {
			var maxIdx = areInOrder ? (candidates.Count - 1) : GetMaxOf(candidates);
			var winner = candidates[maxIdx];

			var best = winner.Current;
			if (first ||
				((last.Stream != best.Stream) && (last.Version != best.Version)) ||
				last.Position != best.Position) {
				last = best;
				sortedCandidates.Add(best);
				first = false;
			}

			if (!winner.MoveNext())
				candidates.RemoveAt(maxIdx);
		}

		return sortedCandidates;
	}

	private static int GetMaxOf(List<IEnumerator<IndexEntry>> enumerators) {
		var max = new IndexEntry(
			stream: ulong.MinValue,
			version: 0,
			position: long.MinValue);

		int idx = 0;
		for (int i = 0; i < enumerators.Count; i++) {
			var cur = enumerators[i].Current;
			if (cur.CompareTo(max) > 0) {
				max = cur;
				idx = i;
			}
		}

		return idx;
	}

	public void Close(bool removeFiles = true) {
		if (!_backgroundRunningEvent.Wait(7000))
			throw new TimeoutException("Could not finish background thread in reasonable time.");
		if (_inMem)
			return;
		if (_indexMap == null) return;
		if (removeFiles) {
			_indexMap.InOrder().ToList().ForEach(x => x.MarkForDestruction());
			var fileName = Path.Combine(_directory, IndexMapFilename);
			if (File.Exists(fileName)) {
				File.SetAttributes(fileName, FileAttributes.Normal);
				File.Delete(fileName);
			}
		} else {
			_indexMap.InOrder().ToList().ForEach(x => x.Dispose());
		}

		_indexMap.InOrder().ToList().ForEach(x => x.WaitForDisposal(TimeSpan.FromMilliseconds(5000)));
	}

	private IndexEntry CreateIndexEntry(IndexKey<TStreamId> key) {
		key = CreateIndexKey(key.StreamId, key.Version, key.Position);
		return new IndexEntry(key.Hash, key.Version, key.Position);
	}

	private ulong CreateHash(TStreamId streamId) {
		return (ulong)_lowHasher.Hash(streamId) << 32 | _highHasher.Hash(streamId);
	}

	/// newest to oldest
	public IEnumerable<ISearchTable> IterateAllInOrder() {
		var awaiting = _awaitingMemTables;
		var map = _indexMap;

		foreach (var tableItem in awaiting)
			yield return tableItem.Table;

		foreach (var table in map.InOrder())
			yield return table;
	}

	private IndexKey<TStreamId> CreateIndexKey(TStreamId streamId, long version, long position) {
		return new IndexKey<TStreamId>(streamId, version, position, CreateHash(streamId));
	}

	private class TableItem {
		public readonly ISearchTable Table;
		public readonly long PrepareCheckpoint;
		public readonly long CommitCheckpoint;
		public readonly int Level;

		public TableItem(ISearchTable table, long prepareCheckpoint, long commitCheckpoint, int level) {
			Table = table;
			PrepareCheckpoint = prepareCheckpoint;
			CommitCheckpoint = commitCheckpoint;
			Level = level;
		}
	}

	private void ForceIndexVerifyOnNextStartup() {
		Log.Debug("Forcing index verification on next startup");
		string path = Path.Combine(_directory, ForceIndexVerifyFilename);
		try {
			using (FileStream fs = new FileStream(path, FileMode.OpenOrCreate)) {
			}

			;
		} catch {
			Log.Error("Could not create force index verification file at: {path}", path);
		}

		return;
	}

	private bool ShouldForceIndexVerify() {
		string path = Path.Combine(_directory, ForceIndexVerifyFilename);
		return File.Exists(path);
	}

	private void DeleteForceIndexVerifyFile() {
		string path = Path.Combine(_directory, ForceIndexVerifyFilename);
		try {
			if (File.Exists(path)) {
				File.SetAttributes(path, FileAttributes.Normal);
				File.Delete(path);
			}
		} catch {
			Log.Error("Could not delete force index verification file at: {path}", path);
		}
	}
}
