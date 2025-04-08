// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.DataStructures;
using KurrentDB.Core.Exceptions;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.Messages;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.Chunks.TFChunk;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.LogCommon;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.Services.Storage.EpochManager;


public abstract class Epochmanager {
}

public class EpochManager<TStreamId> : IEpochManager {
	private static readonly ILogger Log = Serilog.Log.ForContext<Epochmanager>();
	private readonly IPublisher _bus;

	private readonly ICheckpoint _checkpoint;
	private readonly ObjectPool<ITransactionFileReader> _readers;
	private readonly ITransactionFileWriter _writer;
	private readonly IRecordFactory<TStreamId> _recordFactory;
	private readonly INameIndex<TStreamId> _streamNameIndex;
	private readonly INameIndex<TStreamId> _eventTypeIndex;
	private readonly IPartitionManager _partitionManager;
	private readonly Guid _instanceId;

	// We have two async exclusive locks. There are three options to optimize them:
	// 1. Replace exclusive lock with async reader/writer lock to enable horizontal scaling
	// 2. Use copy-on-write immutable collection to avoid lock contention on read side
	// 3. Combine two locks by writing a custom sync primitive using QueuedSynchronizer<T>
	private readonly AsyncExclusiveLock _locker = new();
	private readonly int _cacheSize;
	private readonly LinkedList<EpochRecord> _epochs = new();

	private LinkedListNode<EpochRecord> _firstCachedEpoch;
	private LinkedListNode<EpochRecord> _lastCachedEpoch;

	public EpochRecord GetLastEpoch() => _lastCachedEpoch?.Value;

	public int LastEpochNumber => _lastCachedEpoch?.Value.EpochNumber ?? -1;
	private bool _truncated;
	private readonly AsyncExclusiveLock _truncateLock = new();

	// IMPORTANT
	// Lock ordering to prevent deadlocks:
	// 1)  _locker
	// 2) _truncateLock

	private async ValueTask<long> GetCheckpointAsync(CancellationToken token) {
		await _truncateLock.AcquireAsync(token);
		try {
			if (_truncated)
				throw new InvalidOperationException("Cannot read checkpoint since it has been truncated.");
			return _checkpoint.Read();
		} finally {
			_truncateLock.Release();
		}
	}

	private async ValueTask SetCheckpointAsync(long value, CancellationToken token) {
		await _truncateLock.AcquireAsync(token);
		try {
			if (_truncated)
				throw new InvalidOperationException("Cannot write checkpoint since it has been truncated.");
			_checkpoint.Write(value);
			_checkpoint.Flush();
		} finally {
			_truncateLock.Release();
		}
	}

	public EpochManager(IPublisher bus,
		int cachedEpochCount,
		ICheckpoint checkpoint,
		ITransactionFileWriter writer,
		int initialReaderCount,
		int maxReaderCount,
		Func<ITransactionFileReader> readerFactory,
		IRecordFactory<TStreamId> recordFactory,
		INameIndex<TStreamId> streamNameIndex,
		INameIndex<TStreamId> eventTypeIndex,
		IPartitionManager partitionManager,
		Guid instanceId) {
		Ensure.NotNull(bus, "bus");
		Ensure.Nonnegative(cachedEpochCount, "cachedEpochCount");
		Ensure.NotNull(checkpoint, "checkpoint");
		Ensure.NotNull(writer, "chunkWriter");
		Ensure.Nonnegative(initialReaderCount, "initialReaderCount");
		Ensure.Positive(maxReaderCount, "maxReaderCount");
		if (initialReaderCount > maxReaderCount)
			throw new ArgumentOutOfRangeException(nameof(initialReaderCount),
				"initialReaderCount is greater than maxReaderCount.");
		Ensure.NotNull(readerFactory, "readerFactory");

		_bus = bus;
		_cacheSize = cachedEpochCount;
		_checkpoint = checkpoint;
		_readers = new ObjectPool<ITransactionFileReader>("EpochManager readers pool", initialReaderCount,
			maxReaderCount, readerFactory);
		_writer = writer;
		_recordFactory = recordFactory;
		_streamNameIndex = streamNameIndex;
		_eventTypeIndex = eventTypeIndex;
		_partitionManager = partitionManager;
		_instanceId = instanceId;
	}

	public ValueTask Init(CancellationToken token)
		=> ReadEpochs(_cacheSize, token);

	private async ValueTask ReadEpochs(int maxEpochCount, CancellationToken token) {
		await _locker.AcquireAsync(token);
		try {
			var reader = _readers.Get();
			try {
				long epochPos = await GetCheckpointAsync(token);
				if (epochPos < 0) {
					// we probably have lost/uninitialized epoch checkpoint scan back to find the most recent epoch in the log
					Log.Information("No epoch checkpoint. Scanning log backwards for most recent epoch...");
					reader.Reposition(_writer.FlushedPosition);

					for (SeqReadResult result;
						 (result = await reader.TryReadPrev(token)).Success;
						 token.ThrowIfCancellationRequested()) {
						var rec = result.LogRecord;
						if (rec.RecordType is not LogRecordType.System ||
							((ISystemLogRecord)rec).SystemRecordType is not SystemRecordType.Epoch)
							continue;
						epochPos = rec.LogPosition;
						await SetCheckpointAsync(epochPos, token);
						break;
					}

					Log.Information("Done scanning log backwards for most recent epoch.");
				}

				//read back down the chain of epochs in the log until the cache is full
				int cnt = 0;
				while (epochPos >= 0 && cnt < maxEpochCount) {
					var epoch = await ReadEpochAt(reader, epochPos, token);
					_epochs.AddFirst(epoch);
					if (epoch.EpochPosition == 0) { break; }

					epochPos = epoch.PrevEpochPosition;
					cnt += 1;
				}

				_lastCachedEpoch = _epochs.Last;
				_firstCachedEpoch = _epochs.First;
			} finally {
				_readers.Return(reader);
			}
		} finally {
			_locker.Release();
		}
	}
	private async ValueTask<EpochRecord> ReadEpochAt(ITransactionFileReader reader, long epochPos, CancellationToken token) {
		var result = await reader.TryReadAt(epochPos, couldBeScavenged: false, token);
		if (!result.Success)
			throw new Exception($"Could not find Epoch record at LogPosition {epochPos}.");
		if (result.LogRecord.RecordType != LogRecordType.System)
			throw new Exception($"LogRecord is not SystemLogRecord: {result.LogRecord}.");

		var sysRec = (ISystemLogRecord)result.LogRecord;
		if (sysRec.SystemRecordType != SystemRecordType.Epoch)
			throw new Exception($"SystemLogRecord is not of Epoch sub-type: {result.LogRecord}.");

		return sysRec.GetEpochRecord();
	}
	public async ValueTask<IReadOnlyList<EpochRecord>> GetLastEpochs(int maxCount, CancellationToken token) {
		await _locker.AcquireAsync(token);
		try {
			var res = new List<EpochRecord>();
			var node = _epochs.Last;
			while (node != null && res.Count < maxCount) {
				res.Add(node.Value);
				node = node.Previous;
			}

			return res;
		} finally {
			_locker.Release();
		}
	}

	public async ValueTask<EpochRecord> GetEpochAfter(int epochNumber, bool throwIfNotFound, CancellationToken token) {
		if (epochNumber >= LastEpochNumber) {
			if (!throwIfNotFound)
				return null;
			throw new ArgumentOutOfRangeException(
				nameof(epochNumber),
				$"EpochNumber no epochs exist after Last Epoch {epochNumber}.");
		}

		EpochRecord epoch;
		await _locker.AcquireAsync(token);
		try {
			var epochNode = _epochs.Last;
			while (epochNode != null && epochNode.Value.EpochNumber != epochNumber) {
				epochNode = epochNode.Previous;
			}

			epoch = epochNode?.Next?.Value;
		} finally {
			_locker.Release();
		}

		if (epoch is not null) {
			return epoch; //got it
		}

		var firstEpoch = _firstCachedEpoch?.Value;
		if (firstEpoch != null && firstEpoch.PrevEpochPosition != -1) {
			var reader = _readers.Get();
			try {
				epoch = firstEpoch;
				do {
					var result = await reader.TryReadAt(epoch.PrevEpochPosition, couldBeScavenged: false, token);
					if (!result.Success)
						throw new Exception(
							$"Could not find Epoch record at LogPosition {epoch.PrevEpochPosition}.");
					if (result.LogRecord.RecordType != LogRecordType.System)
						throw new Exception($"LogRecord is not SystemLogRecord: {result.LogRecord}.");

					var sysRec = (ISystemLogRecord)result.LogRecord;
					if (sysRec.SystemRecordType != SystemRecordType.Epoch)
						throw new Exception($"SystemLogRecord is not of Epoch sub-type: {result.LogRecord}.");

					var nextEpoch = sysRec.GetEpochRecord();
					if (nextEpoch.EpochNumber == epochNumber) {
						return epoch; //got it
					}

					epoch = nextEpoch;
				} while (epoch.PrevEpochPosition != -1 && epoch.EpochNumber > epochNumber);

			} finally {
				_readers.Return(reader);
			}
		}

		if (epoch is null && throwIfNotFound) {
			throw new Exception($"Concurrency failure, epoch #{epochNumber} should not be null.");
		}

		return epoch;
	}

	public async ValueTask<bool> IsCorrectEpochAt(long epochPosition, int epochNumber, Guid epochId, CancellationToken token) {
		Ensure.Nonnegative(epochPosition, "logPosition");
		Ensure.Nonnegative(epochNumber, "epochNumber");
		Ensure.NotEmptyGuid(epochId, "epochId");

		if (epochNumber > LastEpochNumber)
			return false;

		EpochRecord epoch;
		await _locker.AcquireAsync(token);
		try {
			epoch = _epochs.FirstOrDefault(e => e.EpochNumber == epochNumber);
			if (epoch != null) {
				return epoch.EpochId == epochId && epoch.EpochPosition == epochPosition;
			}

			if (_firstCachedEpoch.Value is not null && epochNumber > _firstCachedEpoch.Value.EpochNumber)
				// This isn't a cache miss, we don't have that epoch on this node
				return false;
		} finally {
			_locker.Release();
		}

		// epochNumber < _minCachedEpochNumber
		var reader = _readers.Get();
		try {
			var res = await reader.TryReadAt(epochPosition, couldBeScavenged: false, token);
			if (!res.Success || res.LogRecord.RecordType != LogRecordType.System)
				return false;
			var sysRec = (ISystemLogRecord)res.LogRecord;
			if (sysRec.SystemRecordType != SystemRecordType.Epoch)
				return false;

			epoch = sysRec.GetEpochRecord();
			return epoch.EpochNumber == epochNumber && epoch.EpochId == epochId;
		} catch (Exception ex) when (ex is InvalidReadException || ex is UnableToReadPastEndOfStreamException) {
			Log.Information(ex, "Failed to read epoch {epochNumber} at {epochPosition}.", epochNumber, epochPosition);
			return false;
		} finally {
			_readers.Return(reader);
		}
	}

	// This method should be called from single thread.
	public async ValueTask WriteNewEpoch(int epochNumber, CancellationToken token) {
		// Now we write epoch record (with possible retry, if we are at the end of chunk)
		// and update EpochManager's state, by adjusting cache of records, epoch count and un-caching
		// excessive record, if present.
		// If we are writing the very first epoch, last position will be -1.
		if (epochNumber < 0) {
			throw new ArgumentException($"Cannot write an Epoch with a negative Epoch Number {epochNumber}.",
				nameof(epochNumber));
		}

		if (epochNumber <= LastEpochNumber) {
			throw new ArgumentException(
				$"Cannot add Epoch {epochNumber}, new Epoch numbers must be greater than the Last Epoch  {LastEpochNumber}.",
				nameof(epochNumber));
		}

		var epoch = await WriteEpochRecordWithRetry(epochNumber, Guid.NewGuid(),
			_lastCachedEpoch?.Value.EpochPosition ?? -1, _instanceId, token);

		await AddEpochToCache(epoch, token);
	}

	private async ValueTask<EpochRecord> WriteEpochRecordWithRetry(int epochNumber, Guid epochId, long lastEpochPosition,
		Guid instanceId, CancellationToken token) {
		long pos = _writer.Position;
		var epoch = new EpochRecord(pos, epochNumber, epochId, lastEpochPosition, DateTime.UtcNow, instanceId);
		var rec = _recordFactory.CreateEpoch(epoch);

		Log.Debug(
						"=== Writing E{epochNumber}@{epochPosition}:{epochId:B} (previous epoch at {lastEpochPosition}). L={leaderId:B}.",
						epochNumber, epoch.EpochPosition, epochId, lastEpochPosition, epoch.LeaderInstanceId);

		(var written, pos) = await _writer.Write(rec, token);
		if (!written) {
			epoch = new EpochRecord(pos, epochNumber, epochId, lastEpochPosition, DateTime.UtcNow, instanceId);
			rec = _recordFactory.CreateEpoch(epoch);

			if (await _writer.Write(rec, token) is (false, _))
				throw new Exception($"Second write try failed at {epoch.EpochPosition}.");
		}

		await _partitionManager.Initialize(token);
		await WriteEpochInformationWithRetry(epoch, token);
		await _writer.Flush(token);
		_bus.Publish(new ReplicationTrackingMessage.WriterCheckpointFlushed());
		_bus.Publish(new SystemMessage.EpochWritten(epoch));
		return epoch;
	}

	private TStreamId GetEpochInformationStream() {
		if (!_streamNameIndex.GetOrReserve(SystemStreams.EpochInformationStream, out var streamId, out _, out _))
			throw new Exception($"{SystemStreams.EpochInformationStream} stream does not exist");
		return streamId;
	}

	private TStreamId GetEpochInformationEventType() {
		if (!_eventTypeIndex.GetOrReserve(SystemEventTypes.EpochInformation, out var eventTypeId, out _, out _))
			throw new Exception($"{SystemEventTypes.EpochInformation} event type does not exist");
		return eventTypeId;
	}

	async ValueTask WriteEpochInformationWithRetry(EpochRecord epoch, CancellationToken token) {
		if (await TryGetExpectedVersionForEpochInformation(epoch, token) is not { } expectedVersion)
			expectedVersion = ExpectedVersion.NoStream;

		var originalLogPosition = _writer.Position;

		var epochInformation = LogRecord.Prepare(
				factory: _recordFactory,
				logPosition: originalLogPosition,
				correlationId: Guid.NewGuid(),
				eventId: Guid.NewGuid(),
				transactionPos: originalLogPosition,
				transactionOffset: 0,
				eventStreamId: GetEpochInformationStream(),
				expectedVersion: expectedVersion,
				flags: PrepareFlags.SingleWrite | PrepareFlags.IsCommitted | PrepareFlags.IsJson,
				eventType: GetEpochInformationEventType(),
				data: epoch.AsSerialized(),
				metadata: Empty.ByteArray,
                dataSchemaInfo: SchemaInfo.None,
                metadataSchemaInfo: SchemaInfo.None);

		var (written, retryLogPosition) = await _writer.Write(epochInformation, token);
		if (written)
			return;

		epochInformation = epochInformation.CopyForRetry(retryLogPosition, retryLogPosition);

		if (await _writer.Write(epochInformation, token) is (true, _))
			return;

		throw new Exception(
			string.Format("Second write try failed when first writing $epoch-information at {0}, then at {1}.",
				originalLogPosition,
				retryLogPosition));
	}

	// we have just written epoch. about to write the $epoch-information for it.
	// this decides what expected version the $epoch-information should have.
	// it looks up the previous epoch, finds that epoch's previous information
	// (which immediately follows it) and gets its event number.
	// except the first epoch in logv3, which is followed by the root partition
	// initialization before the epochinfo.
	private async ValueTask<long?> TryGetExpectedVersionForEpochInformation(EpochRecord epoch, CancellationToken token) {
		if (epoch.PrevEpochPosition < 0)
			return null;

		var reader = _readers.Get();
		try {
			reader.Reposition(epoch.PrevEpochPosition);

			// read the epoch
			if (await reader.TryReadNext(token) is { Success: false })
				return null;

			// read the epoch-information (if there is one)
			while (true) {
				var result = await reader.TryReadNext(token);
				if (!result.Success)
					return null;

				if (result.LogRecord is IPrepareLogRecord<TStreamId> prepare &&
					EqualityComparer<TStreamId>.Default.Equals(prepare.EventStreamId, GetEpochInformationStream())) {
					// found the epoch information
					return prepare.ExpectedVersion + 1;
				}

				if (result.LogRecord.RecordType is LogRecordType.Prepare
					or LogRecordType.Commit
					or LogRecordType.System
					or LogRecordType.StreamWrite) {
					// definitely not reading the root partition initialization;
					// there is no epochinfo for this epoch (probably the epoch is older
					// than the epochinfo mechanism.
					return null;
				}

				// could be reading the root partition initialization; skip over it.
			}

		} catch (Exception) {
			return null;
		} finally {
			_readers.Return(reader);
		}
	}

	public async ValueTask CacheEpoch(EpochRecord epoch, CancellationToken token) {
		var added = await AddEpochToCache(epoch, token);

		// Check each epoch as it is added to the cache for the first time from the chaser.
		// n.b.: added will be false for idempotent CacheRequests
		// If this check fails, then there is something very wrong with epochs, data corruption is possible.
		if (added && !await IsCorrectEpochAt(epoch.EpochPosition, epoch.EpochNumber, epoch.EpochId, token)) {
			throw new Exception(
				$"Not found epoch at {epoch.EpochPosition} with epoch number: {epoch.EpochNumber} and epoch ID: {epoch.EpochId}. " +
				"SetLastEpoch FAILED! Data corruption risk!");
		}
	}

	/// <summary>
	/// Idempotently adds epochs to the cache
	/// </summary>
	/// <param name="epoch">the epoch to add</param>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>if the submitted epoch was added to the cache, false if already present</returns>
	public async ValueTask<bool> AddEpochToCache(EpochRecord epoch, CancellationToken token) {
		Ensure.NotNull(epoch, "epoch");

		await _locker.AcquireAsync(token);
		try {

			// if it's already cached, just return false to indicate idempotent add
			if (_epochs.Contains(ep => ep.EpochNumber == epoch.EpochNumber)) { return false; }

			//new last epoch written or received, this is the normal case
			//if the list is empty Last will be null
			if (_epochs.Last is null || _epochs.Last.Value.EpochNumber < epoch.EpochNumber) {
				_epochs.AddLast(epoch);
				_lastCachedEpoch = _epochs.Last;
				// in some race conditions we might have a gap in the epoch list
				//read the epochs from the TFLog to fill in the gaps
				if (epoch.EpochPosition > 0 &&
					epoch.PrevEpochPosition >= 0 &&
					epoch.PrevEpochPosition > (_epochs.Last?.Previous?.Value?.EpochPosition ?? -1)) {
					var reader = _readers.Get();
					var previous = _epochs.Last;
					var count = 1; //include last
					try {
						do {
							epoch = await ReadEpochAt(reader, epoch.PrevEpochPosition, token);
							previous = _epochs.AddBefore(previous, epoch);
							count++;
						} while (
							epoch.EpochPosition > 0 &&
							epoch.PrevEpochPosition >= 0 &&
							count <= _cacheSize &&
							epoch.PrevEpochPosition > (previous?.Previous?.Value?.EpochPosition ?? -1));
					} finally {
						_readers.Return(reader);
					}
				}

				while (_epochs.Count > _cacheSize) { _epochs.RemoveFirst(); }

				_firstCachedEpoch = _epochs.First;
				// Now update epoch checkpoint, so on restart we don't scan sequentially TF.
				await SetCheckpointAsync(_epochs.Last.Value.EpochPosition, token);
				Log.Debug(
					"=== Cached new Last Epoch E{epochNumber}@{epochPosition}:{epochId:B} (previous epoch at {lastEpochPosition}) L={leaderId:B}.",
					epoch.EpochNumber, epoch.EpochPosition, epoch.EpochId, epoch.PrevEpochPosition,
					epoch.LeaderInstanceId);
				return true;
			}

			if (epoch.EpochNumber < _epochs.First.Value.EpochNumber) {
				return false;
			}

			//this should never happen
			Log.Error(
				"=== Unable to cache Epoch E{epochNumber}@{epochPosition}:{epochId:B} (previous epoch at {lastEpochPosition}) L={leaderId:B}.",
				epoch.EpochNumber, epoch.EpochPosition, epoch.EpochId, epoch.PrevEpochPosition, epoch.LeaderInstanceId);
			foreach (var epochRecord in _epochs) {
				Log.Error(
					"====== Epoch E{epochNumber}@{epochPosition}:{epochId:B} (previous epoch at {lastEpochPosition}) L={leaderId:B}.",
					epochRecord.EpochNumber, epochRecord.EpochPosition, epochRecord.EpochId,
					epochRecord.PrevEpochPosition, epochRecord.LeaderInstanceId);
			}

			Log.Error(
				"====== Last Epoch E{epochNumber}@{epochPosition}:{epochId:B} (previous epoch at {lastEpochPosition}) L={leaderId:B}.",
				_lastCachedEpoch.Value.EpochNumber, _lastCachedEpoch.Value.EpochPosition,
				_lastCachedEpoch.Value.EpochId, _lastCachedEpoch.Value.PrevEpochPosition,
				_lastCachedEpoch.Value.LeaderInstanceId);
			Log.Error(
				"====== First Epoch E{epochNumber}@{epochPosition}:{epochId:B} (previous epoch at {lastEpochPosition}) L={leaderId:B}.",
				_firstCachedEpoch.Value.EpochNumber, _firstCachedEpoch.Value.EpochPosition,
				_firstCachedEpoch.Value.EpochId, _firstCachedEpoch.Value.PrevEpochPosition,
				_firstCachedEpoch.Value.LeaderInstanceId);

			throw new Exception(
				$"This should never happen: Unable to find correct position to cache Epoch E{epoch.EpochNumber}@{epoch.EpochPosition}:{epoch.EpochId:B} (previous epoch at {epoch.PrevEpochPosition}) L={epoch.LeaderInstanceId:B}");
		} finally {
			_locker.Release();
		}
	}

	private async ValueTask<EpochRecord> TryGetEpochBefore(long position, CancellationToken token) {
		await _locker.AcquireAsync(token);
		try {
			LinkedListNode<EpochRecord> node;
			for (node = _epochs.Last;
				 node is not null && node.Value.EpochPosition >= position;
				 token.ThrowIfCancellationRequested()) {
				node = node.Previous;
			}

			return node?.Value;
		} finally {
			_locker.Release();
		}
	}

	public async ValueTask<EpochRecord> TryTruncateBefore(long position, CancellationToken token) {
		await _truncateLock.AcquireAsync(token);
		try {
			if (_truncated)
				throw new InvalidOperationException("Checkpoint has already been truncated.");
			_truncated = true;
		} finally {
			_truncateLock.Release();
		}

		if (await TryGetEpochBefore(position, token) is not { } epoch)
			return null;

		_checkpoint.Write(epoch.EpochPosition);
		_checkpoint.Flush();

		return epoch;
	}
}