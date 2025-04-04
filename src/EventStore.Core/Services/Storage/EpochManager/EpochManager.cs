// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.DataStructures;
using EventStore.Core.Exceptions;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Messages;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.LogCommon;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Storage.EpochManager;

public abstract class Epochmanager {
}

public class EpochManager<TStreamId> : IEpochManager {
	private static readonly ILogger Log = Serilog.Log.ForContext<EpochManager.Epochmanager>();
	private readonly IPublisher _bus;

	private readonly ICheckpoint _checkpoint;
	private readonly ObjectPool<ITransactionFileReader> _readers;
	private readonly ITransactionFileWriter _writer;
	private readonly IRecordFactory<TStreamId> _recordFactory;
	private readonly INameIndex<TStreamId> _streamNameIndex;
	private readonly INameIndex<TStreamId> _eventTypeIndex;
	private readonly IPartitionManager _partitionManager;
	private readonly Guid _instanceId;

	private readonly object _locker = new object();
	private readonly int _cacheSize;
	private readonly LinkedList<EpochRecord> _epochs = new LinkedList<EpochRecord>();

	private LinkedListNode<EpochRecord> _firstCachedEpoch;
	private LinkedListNode<EpochRecord> _lastCachedEpoch;
	public EpochRecord GetLastEpoch() => _lastCachedEpoch?.Value;
	public int LastEpochNumber => _lastCachedEpoch?.Value.EpochNumber ?? -1;
	private bool _truncated;
	private readonly object _truncateLock = new();

	// IMPORTANT
	// Lock ordering to prevent deadlocks:
	// 1)  _locker
	// 2) _truncateLock

	private long Checkpoint {
		get {
			lock (_truncateLock) {
				if (_truncated)
					throw new InvalidOperationException("Cannot read checkpoint since it has been truncated.");
				return _checkpoint.Read();
			}
		}
		set {
			lock (_truncateLock) {
				if (_truncated)
					throw new InvalidOperationException("Cannot write checkpoint since it has been truncated.");
				_checkpoint.Write(value);
				_checkpoint.Flush();
			}
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

	public void Init() {
		ReadEpochs(_cacheSize);
	}

	private void ReadEpochs(int maxEpochCount) {
		lock (_locker) {
			var reader = _readers.Get();
			try {
				long epochPos = Checkpoint;
				if (epochPos < 0) {
					// we probably have lost/uninitialized epoch checkpoint scan back to find the most recent epoch in the log
					Log.Information("No epoch checkpoint. Scanning log backwards for most recent epoch...");
					reader.Reposition(_writer.FlushedPosition);

					SeqReadResult result;
					while ((result = reader.TryReadPrev()).Success) {
						var rec = result.LogRecord;
						if (rec.RecordType != LogRecordType.System ||
							((ISystemLogRecord)rec).SystemRecordType != SystemRecordType.Epoch)
							continue;
						epochPos = rec.LogPosition;
						break;
					}

					Log.Information("Done scanning log backwards for most recent epoch.");
				}

				//read back down the chain of epochs in the log until the cache is full
				int cnt = 0;
				while (epochPos >= 0 && cnt < maxEpochCount) {
					var epoch = ReadEpochAt(reader, epochPos);
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
		}
	}
	private EpochRecord ReadEpochAt(ITransactionFileReader reader, long epochPos) {
		var result = reader.TryReadAt(epochPos, couldBeScavenged: false);
		if (!result.Success)
			throw new Exception($"Could not find Epoch record at LogPosition {epochPos}.");
		if (result.LogRecord.RecordType != LogRecordType.System)
			throw new Exception($"LogRecord is not SystemLogRecord: {result.LogRecord}.");

		var sysRec = (ISystemLogRecord)result.LogRecord;
		if (sysRec.SystemRecordType != SystemRecordType.Epoch)
			throw new Exception($"SystemLogRecord is not of Epoch sub-type: {result.LogRecord}.");

		return sysRec.GetEpochRecord();
	}
	public EpochRecord[] GetLastEpochs(int maxCount) {
		lock (_locker) {
			var res = new List<EpochRecord>();
			var node = _epochs.Last;
			while (node != null && res.Count < maxCount) {
				res.Add(node.Value);
				node = node.Previous;
			}

			return res.ToArray();
		}
	}

	public EpochRecord GetEpochAfter(int epochNumber, bool throwIfNotFound) {
		if (epochNumber >= LastEpochNumber) {
			if (!throwIfNotFound)
				return null;
			throw new ArgumentOutOfRangeException(
				nameof(epochNumber),
				$"EpochNumber no epochs exist after Last Epoch {epochNumber}.");
		}

		EpochRecord epoch;
		lock (_locker) {
			var epochNode = _epochs.Last;
			while (epochNode != null && epochNode.Value.EpochNumber != epochNumber) {
				epochNode = epochNode.Previous;
			}

			epoch = epochNode?.Next?.Value;
		}

		if (epoch != null) {
			return epoch; //got it
		}

		var firstEpoch = _firstCachedEpoch?.Value;
		if (firstEpoch != null && firstEpoch.PrevEpochPosition != -1) {
			var reader = _readers.Get();
			try {
				epoch = firstEpoch;
				do {
					var result = reader.TryReadAt(epoch.PrevEpochPosition, couldBeScavenged: false);
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

		if (epoch == null && throwIfNotFound) {
			throw new Exception($"Concurrency failure, epoch #{epochNumber} should not be null.");
		}

		return epoch;
	}

	public bool IsCorrectEpochAt(long epochPosition, int epochNumber, Guid epochId) {
		Ensure.Nonnegative(epochPosition, "logPosition");
		Ensure.Nonnegative(epochNumber, "epochNumber");
		Ensure.NotEmptyGuid(epochId, "epochId");

		if (epochNumber > LastEpochNumber)
			return false;

		EpochRecord epoch;
		lock (_locker) {
			epoch = _epochs.FirstOrDefault(e => e.EpochNumber == epochNumber);
			if (epoch != null) {
				return epoch.EpochId == epochId && epoch.EpochPosition == epochPosition;
			}

			if (_firstCachedEpoch.Value != null && epochNumber > _firstCachedEpoch.Value.EpochNumber)
				// This isn't a cache miss, we don't have that epoch on this node
				return false;
		}

		// epochNumber < _minCachedEpochNumber
		var reader = _readers.Get();
		try {
			var res = reader.TryReadAt(epochPosition, couldBeScavenged: false);
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
	public void WriteNewEpoch(int epochNumber) {
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

		var epoch = WriteEpochRecordWithRetry(epochNumber, Guid.NewGuid(),
			_lastCachedEpoch?.Value.EpochPosition ?? -1, _instanceId);
		AddEpochToCache(epoch);
	}

	private EpochRecord WriteEpochRecordWithRetry(int epochNumber, Guid epochId, long lastEpochPosition,
		Guid instanceId) {
		long pos = _writer.Position;
		var epoch = new EpochRecord(pos, epochNumber, epochId, lastEpochPosition, DateTime.UtcNow, instanceId);
		var rec = _recordFactory.CreateEpoch(epoch);

		Log.Debug(
						"=== Writing E{epochNumber}@{epochPosition}:{epochId:B} (previous epoch at {lastEpochPosition}). L={leaderId:B}.",
						epochNumber, epoch.EpochPosition, epochId, lastEpochPosition, epoch.LeaderInstanceId);
		if (!_writer.Write(rec, out pos)) {
			epoch = new EpochRecord(pos, epochNumber, epochId, lastEpochPosition, DateTime.UtcNow, instanceId);
			rec = _recordFactory.CreateEpoch(epoch);

			if (!_writer.Write(rec, out pos))
				throw new Exception($"Second write try failed at {epoch.EpochPosition}.");
		}
		_partitionManager.Initialize();
		WriteEpochInformationWithRetry(epoch);
		_writer.Flush();
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

	void WriteEpochInformationWithRetry(EpochRecord epoch) {
		if (!TryGetExpectedVersionForEpochInformation(epoch, out var expectedVersion))
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
				metadata: Empty.ByteArray);

		if (_writer.Write(epochInformation, out var retryLogPosition))
			return;

		epochInformation = epochInformation.CopyForRetry(retryLogPosition, retryLogPosition);

		if (_writer.Write(epochInformation, out _))
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
	bool TryGetExpectedVersionForEpochInformation(EpochRecord epoch, out long expectedVersion) {
		expectedVersion = default;

		if (epoch.PrevEpochPosition < 0)
			return false;

		var reader = _readers.Get();
		try {
			reader.Reposition(epoch.PrevEpochPosition);

			// read the epoch
			var result = reader.TryReadNext();
			if (!result.Success)
				return false;

			// read the epoch-information (if there is one)
			while (true) {
				result = reader.TryReadNext();
				if (!result.Success)
					return false;

				if (result.LogRecord is IPrepareLogRecord<TStreamId> prepare &&
					EqualityComparer<TStreamId>.Default.Equals(prepare.EventStreamId, GetEpochInformationStream())) {
					// found the epoch information
					expectedVersion = prepare.ExpectedVersion + 1;
					return true;
				}

				if (result.LogRecord.RecordType == LogRecordType.Prepare ||
					result.LogRecord.RecordType == LogRecordType.Commit ||
					result.LogRecord.RecordType == LogRecordType.System ||
					result.LogRecord.RecordType == LogRecordType.StreamWrite) {
					// definitely not reading the root partition initialization;
					// there is no epochinfo for this epoch (probably the epoch is older
					// than the epochinfo mechanism.
					return false;
				}

				// could be reading the root partition initialization; skip over it.
			}

		} catch (Exception) {
			return false;
		} finally {
			_readers.Return(reader);
		}
	}

	public void CacheEpoch(EpochRecord epoch) {
		var added = AddEpochToCache(epoch);
		// Check each epoch as it is added to the cache for the first time from the chaser.
		// n.b.: added will be false for idempotent CacheRequests
		// If this check fails, then there is something very wrong with epochs, data corruption is possible.
		if (added && !IsCorrectEpochAt(epoch.EpochPosition, epoch.EpochNumber, epoch.EpochId)) {
			throw new Exception(
				$"Not found epoch at {epoch.EpochPosition} with epoch number: {epoch.EpochNumber} and epoch ID: {epoch.EpochId}. " +
				"SetLastEpoch FAILED! Data corruption risk!");
		}
	}
	/// <summary>
	/// Idempotently adds epochs to the cache
	/// </summary>
	/// <param name="epoch">the epoch to add</param>
	/// <returns>if the submitted epoch was added to the cache, false if already present</returns>
	public bool AddEpochToCache(EpochRecord epoch) {
		Ensure.NotNull(epoch, "epoch");

		lock (_locker) {

			// if it's already cached, just return false to indicate idempotent add 
			if (_epochs.Contains(ep => ep.EpochNumber == epoch.EpochNumber)) { return false; }

			//new last epoch written or received, this is the normal case
			//if the list is empty Last will be null
			if (_epochs.Last == null || _epochs.Last.Value.EpochNumber < epoch.EpochNumber) {
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
							epoch = ReadEpochAt(reader, epoch.PrevEpochPosition);
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
				Checkpoint = _epochs.Last.Value.EpochPosition;
				Log.Debug(
					"=== Cached new Last Epoch E{epochNumber}@{epochPosition}:{epochId:B} (previous epoch at {lastEpochPosition}) L={leaderId:B}.",
					epoch.EpochNumber, epoch.EpochPosition, epoch.EpochId, epoch.PrevEpochPosition, epoch.LeaderInstanceId);
				return true;
			}
			if (epoch.EpochNumber < _epochs.First.Value.EpochNumber) {
				return false;
			}
			//this should never happen
			Log.Error("=== Unable to cache Epoch E{epochNumber}@{epochPosition}:{epochId:B} (previous epoch at {lastEpochPosition}) L={leaderId:B}.",
				epoch.EpochNumber, epoch.EpochPosition, epoch.EpochId, epoch.PrevEpochPosition, epoch.LeaderInstanceId);
			foreach (var epochRecord in _epochs) {
				Log.Error(
					"====== Epoch E{epochNumber}@{epochPosition}:{epochId:B} (previous epoch at {lastEpochPosition}) L={leaderId:B}.",
					epochRecord.EpochNumber, epochRecord.EpochPosition, epochRecord.EpochId, epochRecord.PrevEpochPosition, epochRecord.LeaderInstanceId);
			}

			Log.Error(
				"====== Last Epoch E{epochNumber}@{epochPosition}:{epochId:B} (previous epoch at {lastEpochPosition}) L={leaderId:B}.",
				_lastCachedEpoch.Value.EpochNumber, _lastCachedEpoch.Value.EpochPosition, _lastCachedEpoch.Value.EpochId, _lastCachedEpoch.Value.PrevEpochPosition, _lastCachedEpoch.Value.LeaderInstanceId);
			Log.Error(
				"====== First Epoch E{epochNumber}@{epochPosition}:{epochId:B} (previous epoch at {lastEpochPosition}) L={leaderId:B}.",
				_firstCachedEpoch.Value.EpochNumber, _firstCachedEpoch.Value.EpochPosition, _firstCachedEpoch.Value.EpochId, _firstCachedEpoch.Value.PrevEpochPosition, _firstCachedEpoch.Value.LeaderInstanceId);

			throw new Exception($"This should never happen: Unable to find correct position to cache Epoch E{epoch.EpochNumber}@{epoch.EpochPosition}:{epoch.EpochId:B} (previous epoch at {epoch.PrevEpochPosition}) L={epoch.LeaderInstanceId:B}");
		}
	}

	private bool TryGetEpochBefore(long position, out EpochRecord epoch) {
		lock (_locker) {
			var node = _epochs.Last;
			while (node != null && node.Value.EpochPosition >= position) {
				node = node.Previous;
			}

			if (node != null) {
				epoch = node.Value;
				return true;
			}

			epoch = null;
			return false;
		}
	}

	public bool TryTruncateBefore(long position, out EpochRecord epoch) {
		lock (_truncateLock) {
			if (_truncated)
				throw new InvalidOperationException("Checkpoint has already been truncated.");
			_truncated = true;
		}

		if (!TryGetEpochBefore(position, out epoch))
			return false;

		_checkpoint.Write(epoch.EpochPosition);
		_checkpoint.Flush();

		return true;
	}
}
