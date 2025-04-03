// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.DataStructures.ProbabilisticFilter;
using EventStore.Core.Index.Hashes;
using EventStore.Core.TransactionLog.Checkpoint;
using Serilog;

namespace EventStore.Core.LogAbstraction.Common;

// This connects a bloom filter datastructure to the rest of the system by
// adding catchup and checkpointing.
public class StreamExistenceFilter :
	INameExistenceFilter {
	private readonly string _filterName;
	private readonly TimeSpan _checkpointDelay;
	private readonly PersistentStreamBloomFilter _persistentBloomFilter;
	private readonly ICheckpoint _checkpoint;
	private readonly Debouncer _checkpointer;
	private long _lastNonFlushedCheckpoint;
	private readonly CancellationTokenSource _cancellationTokenSource;

	private int _initialized;
	private long _addedSinceLoad;

	protected static readonly ILogger Log = Serilog.Log.ForContext<StreamExistenceFilter>();

	public long CurrentCheckpoint {
		get => Interlocked.Read(ref _lastNonFlushedCheckpoint);
		set {
			Interlocked.Exchange(ref _lastNonFlushedCheckpoint, value);
			_checkpointer.Trigger();
		}
	}

	public long CurrentCheckpointFlushed => _checkpoint.Read();

	public string DataFilePath { get; }

	public StreamExistenceFilter(
		string directory,
		ICheckpoint checkpoint,
		string filterName,
		long size,
		TimeSpan checkpointInterval,
		TimeSpan checkpointDelay,
		ILongHasher<string> hasher) {
		_filterName = filterName;
		_checkpointDelay = checkpointDelay;
		_checkpoint = checkpoint;
		_lastNonFlushedCheckpoint = _checkpoint.Read();

		if (!Directory.Exists(directory)) {
			Directory.CreateDirectory(directory);
		}

		DataFilePath = $"{directory}/{_filterName}.dat";
		var create = _lastNonFlushedCheckpoint == -1;

		Log.Information("{filterName}: {creatingOrOpening} {dataFilePath}",
			_filterName, create ? "Creating" : "Opening", DataFilePath);

		try {
			_persistentBloomFilter = new PersistentStreamBloomFilter(
				persistenceStrategy: new FileStreamPersistence(
					size: size,
					path: DataFilePath,
					create: create),
				hasher: hasher,
				corruptionRebuildCount: 0);
		} catch (Exception exc) when (
				exc is CorruptedFileException ||
				exc is CorruptedHashException ||
				exc is SizeMismatchException ||
				exc is FileNotFoundException) {

			var corruptionRebuildCount = 0;

			if (exc is CorruptedFileException) {
				Log.Error(exc, "{filterName} is corrupted. Rebuilding...", _filterName);
			} else if (exc is CorruptedHashException corruptedHashException) {
				corruptionRebuildCount = corruptedHashException.RebuildCount + 1;
				Log.Error(exc, "{filterName} has too many corrupted hashes. Rebuilding...", _filterName);
			} else if (exc is SizeMismatchException) {
				Log.Error(exc, "{filterName} does not have the expected size. Rebuilding...", _filterName);
			} else if (exc is FileNotFoundException) {
				Log.Error(exc, "{filterName} does not exist even though the checkpoint does. Rebuilding...", _filterName);
			}

			File.Delete(DataFilePath);
			_lastNonFlushedCheckpoint = -1L;
			_checkpoint.Write(-1L);
			_checkpoint.Flush();
			_persistentBloomFilter = new PersistentStreamBloomFilter(
				persistenceStrategy: new FileStreamPersistence(
					size: size,
					path: DataFilePath,
					create: true),
				hasher: hasher,
				corruptionRebuildCount: corruptionRebuildCount);
		}

		if (_persistentBloomFilter.CorruptionRebuildCount == 0)
			Log.Information("{filterName} has successfully loaded.", _filterName);
		else
			Log.Information("{filterName} has successfully loaded. Filter has been rebuilt due to hash corruption {count} times.",
				_filterName, _persistentBloomFilter.CorruptionRebuildCount);

		const double p = PersistentBloomFilter.RecommendedFalsePositiveProbability;
		Log.Debug("Optimal number of items for a {filterName} with a configured size of " +
		                "{size:N0} MB is approximately equal to: {n:N0} with false positive probability: {p:N2}",
			_filterName,
			size / 1000 / 1000,
			_persistentBloomFilter.CalculateOptimalNumItems(p),
			p);

		_cancellationTokenSource = new();
		_checkpointer = new Debouncer(
			checkpointInterval,
			async _ => {
				await TakeCheckpointAsync();
			},
			_cancellationTokenSource.Token);
	}

	public void Verify(double corruptionThreshold) => _persistentBloomFilter.Verify(corruptionThreshold);

	private async Task TakeCheckpointAsync() {
		var checkpoint = Interlocked.Read(ref _lastNonFlushedCheckpoint);
		try {
			var prevCheckpoint = _checkpoint.Read();
			var diff = checkpoint - prevCheckpoint;

			var startTime = DateTime.UtcNow;
			Log.Verbose("{filterName} is flushing at {checkpoint:N0}. Diff {diff:N0} ...", _filterName, checkpoint, diff);
			_persistentBloomFilter.Flush();
			var endTime = DateTime.UtcNow;
			Log.Verbose("{filterName} has flushed at {checkpoint:N0}. Diff {diff:N0}. Took {flushLength}",
				       _filterName, checkpoint, diff, endTime - startTime);

			// safety precaution against anything in the stack lying about the data
			// truly being on disk.
			await Task.Delay(_checkpointDelay);

			_checkpoint.Write(checkpoint);
			_checkpoint.Flush();
			Log.Verbose("{filterName} took checkpoint at position: {position:N0}.",
				_filterName,
				_checkpoint.Read());
		} catch (Exception ex) {
			Log.Error(ex, "{filterName} could not take checkpoint at position: {position:N0}", _filterName, checkpoint);
		}
	}

	public async ValueTask Initialize(INameExistenceFilterInitializer source, long truncateToPosition, CancellationToken token) {
		Log.Debug("{filterName} rebuilding started from checkpoint: {checkpoint:N0} (0x{checkpoint:X}).",
			_filterName, CurrentCheckpoint, CurrentCheckpoint);
		var startTime = DateTime.UtcNow;
		await source.Initialize(this, truncateToPosition, token);
		Log.Debug("{filterName} rebuilding done: total processed {processed} records, time elapsed: {elapsed}.",
			_filterName, _addedSinceLoad, DateTime.UtcNow - startTime);
		Interlocked.Exchange(ref _initialized, 1);
	}

	// any truncation must be done prior to calling Add or setting CurrentCheckpoint
	public void TruncateTo(long checkpoint) {
		if (CurrentCheckpoint <= checkpoint) {
			// this was already guarded elsewhere but we want to guard it in this class so that we
			// can guarantee that a checkpoint is only set at all if the filter was successfully
			// flushed. otherwise here we might set the checkpoint even though we hadn't flushed
			// that far.
			Log.Information(
				"{filterName} is NOT truncating from {current:N0} to {truncateTo:N0} " +
				"since it already satisfies the truncation point.",
				_filterName,
				CurrentCheckpoint,
				checkpoint);
			return;
		}

		Log.Information("{filterName} is truncating from {current:N0} to {truncateTo:N0}.",
			_filterName,
			CurrentCheckpoint,
			checkpoint);

		// we dont need to remove data from the bloom filter since false positives
		// are allowed, and we can't anyway, since we wouldn't know what to remove.
		// but we DO need to move the checkpoint back and flush it. otherwise if
		// (1) the writer/chaser move forward and (2) the node exits before
		// the filter has flushed those records, then we will no longer know that
		// we need to go back and add them.
		Interlocked.Exchange(ref _lastNonFlushedCheckpoint, checkpoint);
		_checkpoint.Write(checkpoint);
		_checkpoint.Flush();
	}

	public void Add(string name) {
		_persistentBloomFilter.Add(name);
		_addedSinceLoad++;
	}

	public void Add(ulong hash) {
		_persistentBloomFilter.Add(hash);
		_addedSinceLoad++;
	}

	public bool MightContain(string name) {
		if (Interlocked.CompareExchange(ref _initialized, 0, 0) == 0)
			throw new InvalidOperationException("Initialize the filter before querying");
		return _persistentBloomFilter.MightContain(name);
	}

	public void Dispose() {
		_cancellationTokenSource?.Cancel();
		_persistentBloomFilter?.Dispose();
		GC.SuppressFinalize(this);
	}
}
