// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;
using EventStore.Core.DataStructures;
using EventStore.Core.LogAbstraction;
using EventStore.Core.LogAbstraction.Common;
using FASTER.core;
using Serilog;
using Value = System.UInt32;
using static System.Threading.Timeout;

namespace EventStore.Core.LogV3.FASTER;

public class NameIndexPersistence {
	protected static readonly ILogger Log = Serilog.Log.ForContext<NameIndexPersistence>();
}

public class FASTERNameIndexPersistence :
	NameIndexPersistence,
	INameIndexPersistence<Value> {

	private static readonly Encoding _utf8NoBom = new UTF8Encoding(false, true);
	private readonly IDevice _log;
	private readonly FasterKV<SpanByte, Value> _store;
	private readonly LogSettings _logSettings;
	private readonly Debouncer _logCheckpointer;
	private readonly CancellationTokenSource _cancellationTokenSource;

	// used during initialization
	private readonly ClientSession<SpanByte, Value, Value, Value, Empty, MaintenanceFunctions<Value>> _maintenanceSession;

	// used during initialization and then by the confirmer
	private readonly ClientSession<SpanByte, Value, Value, Value, Empty, WriterFunctions<Value>> _writerSession;

	// used during initialization and then by various readers (including the storage writer)
	private readonly ObjectPool<ReaderSession<Value>> _readerSessionPool;
	private readonly string _indexName;
	private readonly Value _firstValue;
	private readonly Value _valueInterval;

	// not expecting any contention for _lastValueAdded but do need the memory barrriers
	private readonly AsyncExclusiveLock _lastValueLock = new();
	private Value _lastValueAdded;

	private bool _disposed = false;

	public Value LastValueAdded => _lastValueAdded;

	public FASTERNameIndexPersistence(
		string indexName,
		string logDir,
		Value firstValue,
		Value valueInterval,
		int initialReaderCount,
		int maxReaderCount,
		bool enableReadCache,
		TimeSpan checkpointInterval) {

		_indexName = indexName;
		_firstValue = firstValue;
		_valueInterval = valueInterval;

		_log = Devices.CreateLogDevice(
			logPath: $"{logDir}/{_indexName}.log",
			deleteOnClose: false);

		var checkpointSettings = new CheckpointSettings {
			CheckpointDir = $"{logDir}",
			RemoveOutdated = true,
		};

		var readCacheSettings = enableReadCache
			? new ReadCacheSettings {
				// todo: dynamic settings according to available memory
				MemorySizeBits = 15,
				PageSizeBits = 12,
				SecondChanceFraction = 0.1
			}
			: null;

		_logSettings = new LogSettings {
			LogDevice = _log,
			// todo: dynamic settings according to available memory
			MemorySizeBits = 15,
			PageSizeBits = 12,
			//PreallocateLog = true,

			ReadCacheSettings = readCacheSettings,
		};

		_store = new FasterKV<SpanByte, Value>(
			// todo: dynamic settings according to available memory
			// but bear in mind if we have taken an index checkpoint there is a
			// procedure to resize it.
			size: 1L << 20,
			checkpointSettings: checkpointSettings,
			logSettings: _logSettings);

		_writerSession = _store
			.For(new WriterFunctions<Value>())
			.NewSession<WriterFunctions<Value>>();

		_maintenanceSession = _store
			.For(new MaintenanceFunctions<Value>())
			.NewSession<MaintenanceFunctions<Value>>();

		_readerSessionPool = new ObjectPool<ReaderSession<Value>>(
			objectPoolName: $"{_indexName} readers pool",
			initialCount: initialReaderCount,
			maxCount: maxReaderCount,
			factory: () => new ReaderSession<Value>(
				_store.For(new ReaderFunctions<Value>()).NewSession<ReaderFunctions<Value>>(),
				new Context<Value>()),
			dispose: session => session.Dispose());

		_cancellationTokenSource = new();
		_logCheckpointer = new Debouncer(
			checkpointInterval,
			async token => {
				try {
					var success = await CheckpointLogAsync();
					if (!success)
						throw new Exception($"not successful");

					Log.Debug("{_indexName} took checkpoint", _indexName);
				} catch (Exception ex) {
					Log.Error(ex, "{_indexName} could not take checkpoint", _indexName);
				}
			},
			_cancellationTokenSource.Token);

		Log.Information("{indexName} total memory before recovery {totalMemoryMib:N0} MiB.", _indexName, CalcTotalMemoryMib());
		Recover();
		Log.Information("{indexName} total memory after recovery {totalMemoryMib:N0} MiB.", _indexName, CalcTotalMemoryMib());
	}

	public void Dispose() {
		if (_disposed)
			return;
		_disposed = true;
		_cancellationTokenSource.Cancel();
		_maintenanceSession?.Dispose();
		_writerSession?.Dispose();
		_readerSessionPool?.Dispose();
		_store?.Dispose();
		_log?.Dispose();
	}

	void Recover() {
		try {
			_store.Recover();
			var (name, value) = ScanBackwards().FirstOrDefault();

			_lastValueLock.TryAcquire(InfiniteTimeSpan);
			try {
				_lastValueAdded = name == null ? default : value;
			} finally {
				_lastValueLock.Release();
			}

			Log.Information(
				"{indexName} has been recovered. " +
				"Last entry was {name}:{value}",
				_indexName, name, value);
		} catch (FasterException ex) {
			Log.Information($"{_indexName} is starting from scratch: {ex.Message}");
		}
	}

	// the source has been initialised. it now contains exactly the data that we want to have in the name index.
	// there are three cases
	// 1. we are exactly in sync with the source (do nothing)
	// 2. we are behind the source (could be by a lot, say we are rebuilding) -> catch up
	// 3. we are ahead of the source (should only be by a little) -> truncate
	// after we make it that the indexes only contain replicated records then there
	// will be no need for the truncation case and we will remove it with the associated
	// scanning code.
	public async ValueTask Init(INameLookup<Value> source, CancellationToken token) {
		Log.Information("{indexName} initializing...", _indexName);
		var iter = ScanBackwards().GetEnumerator();

		if (!iter.MoveNext()) {
			Log.Information("{indexName} is empty. Catching up from beginning of source.", _indexName);
			await CatchUp(source, previousValue: 0, token);

		} else if (await source.LookupName(iter.Current.Value, token) is { } sourceName) {
			if (sourceName != iter.Current.Name)
				ThrowNameMismatch(iter.Current.Value, iter.Current.Name, sourceName);

			Log.Information("{indexName} has entry {value}. Catching up from there", _indexName, iter.Current.Value);
			await CatchUp(source, previousValue: iter.Current.Value, token);

		} else {
			// we have a most recent entry but it is not in the source.
			// scan backwards until we find something in common with the source and
			// truncate everything in between.
			var keysToTruncate = new List<string>();
			var keysToTruncateSet = new HashSet<string>();

			void PopEntry() {
				Debug.Assert(_lastValueLock.IsLockHeld);

				var name = iter.Current.Name;
				var value = iter.Current.Value;
				if (keysToTruncateSet.Add(name)) {
					keysToTruncate.Add(iter.Current.Name);

					if (_lastValueAdded != value)
						throw new Exception(
							$"Trying to remove the last entry added \"{name}\":{value} but the last entry was really {_lastValueAdded}");

					_lastValueAdded = _lastValueAdded > _firstValue
						? _lastValueAdded - _valueInterval
						: default;

					Log.Verbose("{indexName} is going to delete {name}:{value}", _indexName, name, value);
				}
			}

			await _lastValueLock.AcquireAsync(token);
			try {
				PopEntry();
			} finally {
				_lastValueLock.Release();
			}

			bool found = false;
			await _lastValueLock.AcquireAsync(token);
			sourceName = null;
			try {
				while (!found && iter.MoveNext()) {
					sourceName = await source.LookupName(iter.Current.Value, token);
					if (sourceName is null) {
						PopEntry();
					} else {
						found = true;
					}
				}
			} finally {
				_lastValueLock.Release();
			}

			var last = await source.TryGetLastValue(token);
			if (found) {
				if (iter.Current.Value != last.Value)
					throw new Exception($"{_indexName} this should never happen. expecting source to have values up to {iter.Current.Value} but was {last}");
				if (iter.Current.Name != sourceName)
					ThrowNameMismatch(iter.Current.Value, iter.Current.Name, sourceName);
			} else {
				if (last.HasValue)
					throw new Exception($"{_indexName} this should never happen. expecting source to have been empty but it wasn't. has values up to {last}");
			}

			Log.Information("{indexName} is truncating {count} entries", _indexName, keysToTruncate.Count);
			Truncate(keysToTruncate);
		}

		CheckpointLogSynchronously();

		await _lastValueLock.AcquireAsync(token);
		try {
			Log.Information("{indexName} initialized. Last value is {value}", _indexName, _lastValueAdded);
		} finally {
			_lastValueLock.Release();
		}

		Log.Information("{indexName} total memory after initialization {totalMemoryMib:N0} MiB.", _indexName, CalcTotalMemoryMib());
	}

	void ThrowNameMismatch(Value value, string ourName, string sourceName) {
		throw new Exception(
			$"{_indexName} this should never happen. name mismatch. " +
			$"value: {value} name: \"{ourName}\"/\"{sourceName}\"");
	}

	async ValueTask CatchUp(INameLookup<Value> source, Value previousValue, CancellationToken token) {
		if (!(await source.TryGetLastValue(token)).TryGet(out var sourceLastValue)) {
			Log.Information("{indexName} source is empty, nothing to catch up", _indexName);
			return;
		}

		Log.Information("{indexName} source last value is {sourceLastValue}", _indexName, sourceLastValue);

		var startValue = previousValue == 0
			? _firstValue
			: previousValue + _valueInterval;

		var count = 0;
		for (var sourceValue = startValue; sourceValue <= sourceLastValue; sourceValue += _valueInterval) {
			count++;
			if (await source.LookupName(sourceValue, token) is not { } name)
				throw new Exception($"{_indexName} this should never happen. could not find {sourceValue} in source");

			Add(name, sourceValue);
		}

		Log.Information("{indexName} caught up {count} entries", _indexName, count);
	}

	void Truncate(IList<string> names) {
		for (int i = 0; i < names.Count; i++) {
			TruncateTail(names[i]);
		}
	}

	void TruncateTail(string name) {
		if (string.IsNullOrEmpty(name))
			throw new ArgumentNullException(nameof(name));

		// convert the name into a UTF8 span which we can use as a key.
		Span<byte> keySpan = stackalloc byte[Measure(name)];
		PopulateKey(name, keySpan);

		var status = _maintenanceSession.Delete(key: SpanByte.FromFixedSpan(keySpan));

		switch (status) {
			case Status.OK:
			case Status.NOTFOUND:
				break;

			case Status.PENDING:
				_maintenanceSession.CompletePending(wait: true);
				break;

			case Status.ERROR:
			default:
				throw new Exception($"{_indexName} Unexpected status {status} deleting {name}");
		}
	}

	/// this will include the same name more than once if it was truncated and then
	/// subsequently re-added with the same value. de-dup when consuming if necessary.
	public IEnumerable<(string Name, Value Value)> Scan() => Scan(0, _store.Log.TailAddress);

	public IEnumerable<(string Name, Value Value)> Scan(long beginAddress, long endAddress) {
		using var iter = _store.Log.Scan(beginAddress, endAddress);
		while (iter.GetNext(out var recordInfo)) {
			if (recordInfo.Invalid || recordInfo.Tombstone)
				continue;

			var name = _utf8NoBom.GetString(iter.GetKey().AsReadOnlySpan());
			var value = iter.GetValue();

			if (!TryGetValue(name, out var currentValue) || currentValue != value) {
				// deleted (from a previous truncation), and maybe readded with a different value
				continue;
			}

			yield return (name, value);
		}
	}

	public IEnumerable<(string Name, Value Value)> ScanBackwards() {
		// in faster you can begin log scans from records address, but also from
		// page boundaries. use this to iterate backwards by jumping back
		// one page at a time.
		var pageSize = 1 << _logSettings.PageSizeBits;
		var endAddress = _store.Log.TailAddress;
		var beginAddress = endAddress / pageSize * pageSize;

		if (beginAddress < 0)
			beginAddress = 0;

		var detailCount = 0;
		while (endAddress > 0) {
			var entries = Scan(beginAddress, endAddress).ToList();
			for (int i = entries.Count - 1; i >= 0; i--) {
				yield return entries[i];
				detailCount++;
			}
			endAddress = beginAddress;
			beginAddress -= pageSize;
		}
	}

	public Value LookupValue(string name) {
		if (string.IsNullOrEmpty(name))
			throw new ArgumentNullException(nameof(name));

		return TryGetValue(name, out var value) ? value : default;
	}

	public bool TryGetValue(string name, out Value value) {
		using var lease = _readerSessionPool.Rent();
		var session = lease.Reader.ClientSession;
		var context = lease.Reader.Context;

		// convert the name into a UTF8 span which we can use as a key.
		Span<byte> key = stackalloc byte[Measure(name)];
		PopulateKey(name, key);

		var status = session.Read(
			key: SpanByte.FromFixedSpan(key),
			output: out value,
			userContext: context);

		switch (status) {
			case Status.OK: return true;
			case Status.NOTFOUND: return false;
			case Status.PENDING:
				session.CompletePending(wait: true);
				switch (context.Status) {
					case Status.OK:
						value = context.Value;
						return true;
					case Status.NOTFOUND: return false;
					default: throw new Exception($"{_indexName} Unexpected status {context.Status} completing read for \"{name}\"");
				}
			case Status.ERROR:
			default:
				throw new Exception($"{_indexName} Unexpected status {status} reading \"{name}\"");
		}
	}

	public void Add(string name, Value value) {
		if (string.IsNullOrEmpty(name))
			throw new ArgumentNullException(nameof(name));

		_lastValueLock.TryAcquire(InfiniteTimeSpan);
		try {
			var validFirst = _lastValueAdded == default && value == _firstValue;
			var validNext = value == _lastValueAdded + _valueInterval;
			if (!validFirst && !validNext)
				throw new Exception(
					$"{_indexName} attempting to add entry out of order: \"{name}\":{value} when last value added was {_lastValueAdded}");
		} finally {
			_lastValueLock.Release();
		}

		// convert the name into a UTF8 span which we can use as a key in FASTER.
		Span<byte> key = stackalloc byte[Measure(name)];
		PopulateKey(name, key);

		var status = _writerSession.Upsert(
			key: SpanByte.FromFixedSpan(key),
			desiredValue: value);

		switch (status) {
			// pending fine too, they will complete later in order, any that dont
			// complete will catch up on startup
			case Status.OK:
			case Status.NOTFOUND:
			case Status.PENDING:
				break;
			case Status.ERROR:
			default:
				throw new Exception($"{_indexName} Unexpected status {status} upserting \"{name}\":{value}");
		}

		_lastValueLock.TryAcquire(InfiniteTimeSpan);
		try {
			_lastValueAdded = value;
		} finally {
			_lastValueLock.Release();
		}

		_logCheckpointer.Trigger();

		Log.Verbose("{indexName} added new entry: {name}:{value}", _indexName, name, value);
	}

	void CheckpointLogSynchronously() {
		// unideal
		CheckpointLogAsync().GetAwaiter().GetResult();
	}

	public async Task<bool> CheckpointLogAsync() {
		LogStats();
		Log.Debug("{indexName} is checkpointing", _indexName);
		var (success, _) = await _store.TakeHybridLogCheckpointAsync(CheckpointType.FoldOver);
		return success;
	}

	void LogStats() {
		Log.Debug("{indexName} total memory {totalMemoryMib:N0} MiB.", _indexName, CalcTotalMemoryMib());
	}

	long CalcTotalMemoryMib() {
		var indexSizeBytes = _store.IndexSize * 64;
		var indexOverflowBytes = _store.OverflowBucketCount * 64;
		var logMemorySizeBytes = _store.Log.MemorySizeBytes;
		var readCacheMemorySizeBytes = _store.ReadCache?.MemorySizeBytes ?? 0;
		var totalMemoryBytes = indexSizeBytes + indexOverflowBytes + logMemorySizeBytes + readCacheMemorySizeBytes;
		var totalMemoryMib = totalMemoryBytes / 1024 / 1024;
		return totalMemoryMib;
	}

	static int Measure(string source) {
		var length = _utf8NoBom.GetByteCount(source);
		return length;
	}

	static void PopulateKey(string source, Span<byte> destination) {
		Utf8.FromUtf16(source, destination, out _, out _, true, true);
	}
}
