// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.DataStructures;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Settings;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.LogCommon;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Storage.ReaderIndex;

public interface IIndexWriter<TStreamId> {
	long CachedTransInfo { get; }
	long NotCachedTransInfo { get; }

	void Reset();
	CommitCheckResult<TStreamId> CheckCommitStartingAt(long transactionPosition, long commitPosition);
	CommitCheckResult<TStreamId> CheckCommit(TStreamId streamId, long expectedVersion, IEnumerable<Guid> eventIds, bool streamMightExist);
	void PreCommit(CommitLogRecord commit);
	void PreCommit(ReadOnlySpan<IPrepareLogRecord<TStreamId>> commitedPrepares);
	void UpdateTransactionInfo(long transactionId, long logPosition, TransactionInfo<TStreamId> transactionInfo);
	TransactionInfo<TStreamId> GetTransactionInfo(long writerCheckpoint, long transactionId);
	void PurgeNotProcessedCommitsTill(long checkpoint);
	void PurgeNotProcessedTransactions(long checkpoint);

	bool IsSoftDeleted(TStreamId streamId);
	long GetStreamLastEventNumber(TStreamId streamId);
	StreamMetadata GetStreamMetadata(TStreamId streamId);
	RawMetaInfo GetStreamRawMeta(TStreamId streamId);

	TStreamId GetStreamId(string streamName);
	string GetStreamName(TStreamId streamId);
}

public struct RawMetaInfo {
	public readonly long MetaLastEventNumber;
	public readonly ReadOnlyMemory<byte> RawMeta;

	public RawMetaInfo(long metaLastEventNumber, ReadOnlyMemory<byte> rawMeta) {
		MetaLastEventNumber = metaLastEventNumber;
		RawMeta = rawMeta;
	}
}

public abstract class IndexWriter {
	protected static readonly ILogger Log = Serilog.Log.ForContext<IndexWriter>();
}

public class IndexWriter<TStreamId> : IndexWriter, IIndexWriter<TStreamId> {
	private static EqualityComparer<TStreamId> StreamIdComparer { get; } = EqualityComparer<TStreamId>.Default;

	public long CachedTransInfo {
		get { return Interlocked.Read(ref _cachedTransInfo); }
	}

	public long NotCachedTransInfo {
		get { return Interlocked.Read(ref _notCachedTransInfo); }
	}

	private readonly IIndexBackend _indexBackend;
	private readonly IIndexReader<TStreamId> _indexReader;
	private readonly IValueLookup<TStreamId> _streamIds;
	private readonly INameLookup<TStreamId> _streamNames;
	private readonly ISystemStreamLookup<TStreamId> _systemStreams;
	private readonly TStreamId _emptyStreamId;
	private readonly IStickyLRUCache<long, TransactionInfo<TStreamId>> _transactionInfoCache =
		new StickyLRUCache<long, TransactionInfo<TStreamId>>(ESConsts.TransactionMetadataCacheCapacity);

	private readonly Queue<TransInfo> _notProcessedTrans = new Queue<TransInfo>();

	private readonly BoundedCache<Guid, EventInfo> _committedEvents;

	private readonly IStickyLRUCache<TStreamId, long> _streamVersions =
		new StickyLRUCache<TStreamId, long>(ESConsts.IndexWriterCacheCapacity);

	private readonly IStickyLRUCache<TStreamId, StreamMeta>
		_streamRawMetas =
			new StickyLRUCache<TStreamId, StreamMeta>(0); // store nothing flushed, only sticky non-flushed stuff

	private readonly Queue<CommitInfo> _notProcessedCommits = new Queue<CommitInfo>();

	private long _cachedTransInfo;
	private long _notCachedTransInfo;

	public IndexWriter(
		IIndexBackend indexBackend,
		IIndexReader<TStreamId> indexReader,
		IValueLookup<TStreamId> streamIds,
		INameLookup<TStreamId> streamNames,
		ISystemStreamLookup<TStreamId> systemStreams,
		TStreamId emptyStreamId,
		ISizer<TStreamId> inMemorySizer) {
		Ensure.NotNull(indexBackend, "indexBackend");
		Ensure.NotNull(indexReader, "indexReader");
		Ensure.NotNull(streamIds, nameof(streamIds));
		Ensure.NotNull(streamNames, nameof(streamNames));
		Ensure.NotNull(systemStreams, nameof(systemStreams));
		Ensure.NotNull(inMemorySizer, nameof(inMemorySizer));

		_committedEvents = new BoundedCache<Guid, EventInfo>(int.MaxValue, ESConsts.CommitedEventsMemCacheLimit,
			x => 16 + 4 + IntPtr.Size + inMemorySizer.GetSizeInBytes(x.StreamId));
		_indexBackend = indexBackend;
		_indexReader = indexReader;
		_streamIds = streamIds;
		_streamNames = streamNames;
		_systemStreams = systemStreams;
		_emptyStreamId = emptyStreamId;
	}

	public void Reset() {
		_notProcessedCommits.Clear();
		_streamVersions.Clear();
		_streamRawMetas.Clear();
		_notProcessedTrans.Clear();
		_transactionInfoCache.Clear();
	}

	public CommitCheckResult<TStreamId> CheckCommitStartingAt(long transactionPosition, long commitPosition) {
		TStreamId streamId;
		long expectedVersion;
		using (var reader = _indexBackend.BorrowReader()) {
			try {
				var prepare = GetPrepare(reader, transactionPosition);
				if (prepare == null) {
					Log.Error("Could not read first prepare of to-be-committed transaction. "
							  + "Transaction pos: {transactionPosition}, commit pos: {commitPosition}.",
						transactionPosition, commitPosition);
					var message = String.Format("Could not read first prepare of to-be-committed transaction. "
												+ "Transaction pos: {0}, commit pos: {1}.",
						transactionPosition, commitPosition);
					throw new InvalidOperationException(message);
				}

				streamId = prepare.EventStreamId;
				expectedVersion = prepare.ExpectedVersion;
			} catch (InvalidOperationException) {
				return new CommitCheckResult<TStreamId>(CommitDecision.InvalidTransaction, _emptyStreamId, -1, -1, -1, false);
			}
		}

		// we should skip prepares without data, as they don't mean anything for idempotency
		// though we have to check deletes, otherwise they always will be considered idempotent :)
		var eventIds = from prepare in GetTransactionPrepares(transactionPosition, commitPosition)
					   where prepare.Flags.HasAnyOf(PrepareFlags.Data | PrepareFlags.StreamDelete)
					   select prepare.EventId;
		return CheckCommit(streamId, expectedVersion, eventIds, streamMightExist: true);
	}

	private static IPrepareLogRecord<TStreamId> GetPrepare(TFReaderLease reader, long logPosition) {
		RecordReadResult result = reader.TryReadAt(logPosition, couldBeScavenged: true);
		if (!result.Success)
			return null;
		if (result.LogRecord.RecordType != LogRecordType.Prepare)
			throw new Exception(string.Format("Incorrect type of log record {0}, expected Prepare record.",
				result.LogRecord.RecordType));
		return (IPrepareLogRecord<TStreamId>)result.LogRecord;
	}

	private CommitCheckResult<TStreamId> CheckCommitForNewStream(TStreamId streamId, long expectedVersion) {
		var commitDecision = expectedVersion switch {
			ExpectedVersion.Any or ExpectedVersion.NoStream => CommitDecision.Ok,
			_ => CommitDecision.WrongExpectedVersion
		};

		return new CommitCheckResult<TStreamId>(commitDecision, streamId, ExpectedVersion.NoStream, -1, -1, false);
	}

	public CommitCheckResult<TStreamId> CheckCommit(TStreamId streamId, long expectedVersion, IEnumerable<Guid> eventIds, bool streamMightExist) {
		if (!streamMightExist) {
			// fast path for completely new streams
			return CheckCommitForNewStream(streamId, expectedVersion);
		}

		var curVersion = GetStreamLastEventNumber(streamId);
		if (curVersion == EventNumber.DeletedStream)
			return new CommitCheckResult<TStreamId>(CommitDecision.Deleted, streamId, curVersion, -1, -1, false);
		if (curVersion == EventNumber.Invalid)
			return new CommitCheckResult<TStreamId>(CommitDecision.WrongExpectedVersion, streamId, curVersion, -1, -1, false);

		if (expectedVersion == ExpectedVersion.StreamExists) {
			if (IsSoftDeleted(streamId))
				return new CommitCheckResult<TStreamId>(CommitDecision.Deleted, streamId, curVersion, -1, -1, true);

			if (curVersion < 0) {
				var metadataVersion = GetStreamLastEventNumber(_systemStreams.MetaStreamOf(streamId));
				if (metadataVersion < 0)
					return new CommitCheckResult<TStreamId>(CommitDecision.WrongExpectedVersion, streamId, curVersion, -1, -1,
						false);
			}
		}

		// idempotency checks
		if (expectedVersion == ExpectedVersion.Any || expectedVersion == ExpectedVersion.StreamExists) {
			var first = true;
			long startEventNumber = -1;
			long endEventNumber = -1;
			foreach (var eventId in eventIds) {
				EventInfo prepInfo;
				if (!_committedEvents.TryGetRecord(eventId, out prepInfo) || !StreamIdComparer.Equals(prepInfo.StreamId, streamId))
					return new CommitCheckResult<TStreamId>(first ? CommitDecision.Ok : CommitDecision.CorruptedIdempotency,
						streamId, curVersion, -1, -1, first && IsSoftDeleted(streamId));
				if (first)
					startEventNumber = prepInfo.EventNumber;
				endEventNumber = prepInfo.EventNumber;
				first = false;
			}

			if (first) /*no data in transaction*/
				return new CommitCheckResult<TStreamId>(CommitDecision.Ok, streamId, curVersion, -1, -1, IsSoftDeleted(streamId));
			else {
				var isReplicated = _indexReader.GetStreamLastEventNumber(streamId) >= endEventNumber;
				//TODO(clc): the new index should hold the log positions removing this read
				//n.b. the index will never have the event in the case of NotReady as it only committed records are indexed
				//in that case the position will need to come from the pre-index
				var idempotentEvent = _indexReader.ReadEvent(IndexReader.UnspecifiedStreamName, streamId, endEventNumber);
				var logPos = idempotentEvent.Result == ReadEventResult.Success
					? idempotentEvent.Record.LogPosition : -1;
				if (isReplicated)
					return new CommitCheckResult<TStreamId>(CommitDecision.Idempotent, streamId, curVersion, startEventNumber, endEventNumber, false, logPos);
				else
					return new CommitCheckResult<TStreamId>(CommitDecision.IdempotentNotReady, streamId, curVersion, startEventNumber, endEventNumber, false, logPos);
			}
		}

		if (expectedVersion < curVersion) {
			var eventNumber = expectedVersion;
			foreach (var eventId in eventIds) {
				eventNumber += 1;

				EventInfo prepInfo;
				if (_committedEvents.TryGetRecord(eventId, out prepInfo)
					&& StreamIdComparer.Equals(prepInfo.StreamId, streamId)
					&& prepInfo.EventNumber == eventNumber)
					continue;

				var res = _indexReader.ReadPrepare(streamId, eventNumber);
				if (res != null && res.EventId == eventId)
					continue;

				var first = eventNumber == expectedVersion + 1;
				if (!first)
					return new CommitCheckResult<TStreamId>(CommitDecision.CorruptedIdempotency, streamId, curVersion, -1, -1,
						false);

				if (expectedVersion == ExpectedVersion.NoStream && IsSoftDeleted(streamId))
					return new CommitCheckResult<TStreamId>(CommitDecision.Ok, streamId, curVersion, -1, -1, true);

				return new CommitCheckResult<TStreamId>(CommitDecision.WrongExpectedVersion, streamId, curVersion, -1, -1,
					false);
			}

			if (eventNumber == expectedVersion) /* no data in transaction */
				return new CommitCheckResult<TStreamId>(CommitDecision.WrongExpectedVersion, streamId, curVersion, -1, -1, false);
			else {
				var isReplicated = _indexReader.GetStreamLastEventNumber(streamId) >= eventNumber;
				//TODO(clc): the new index should hold the log positions removing this read
				//n.b. the index will never have the event in the case of NotReady as it only committed records are indexed
				//in that case the position will need to come from the pre-index
				var idempotentEvent = _indexReader.ReadEvent(IndexReader.UnspecifiedStreamName, streamId, eventNumber);
				var logPos = idempotentEvent.Result == ReadEventResult.Success
					? idempotentEvent.Record.LogPosition : -1;
				if (isReplicated)
					return new CommitCheckResult<TStreamId>(CommitDecision.Idempotent, streamId, curVersion, expectedVersion + 1, eventNumber, false, logPos);
				else
					return new CommitCheckResult<TStreamId>(CommitDecision.IdempotentNotReady, streamId, curVersion, expectedVersion + 1, eventNumber, false, logPos);
			}
		}

		if (expectedVersion > curVersion)
			return new CommitCheckResult<TStreamId>(CommitDecision.WrongExpectedVersion, streamId, curVersion, -1, -1, false);

		// expectedVersion == currentVersion
		return new CommitCheckResult<TStreamId>(CommitDecision.Ok, streamId, curVersion, -1, -1, IsSoftDeleted(streamId));
	}

	public void PreCommit(CommitLogRecord commit) {
		TStreamId streamId = default;
		long eventNumber = EventNumber.Invalid;
		IPrepareLogRecord<TStreamId> lastPrepare = null;

		foreach (var prepare in GetTransactionPrepares(commit.TransactionPosition, commit.LogPosition)) {
			if (prepare.Flags.HasNoneOf(PrepareFlags.StreamDelete | PrepareFlags.Data))
				continue;

			if (StreamIdComparer.Equals(streamId, default))
				streamId = prepare.EventStreamId;

			if (!StreamIdComparer.Equals(prepare.EventStreamId, streamId))
				throw new Exception(string.Format("Expected stream: {0}, actual: {1}.", streamId,
					prepare.EventStreamId));

			eventNumber = prepare.Flags.HasAnyOf(PrepareFlags.StreamDelete)
				? EventNumber.DeletedStream
				: commit.FirstEventNumber + prepare.TransactionOffset;
			lastPrepare = prepare;
			_committedEvents.PutRecord(prepare.EventId, new EventInfo(streamId, eventNumber),
				throwOnDuplicate: false);
		}

		if (eventNumber != EventNumber.Invalid)
			_streamVersions.Put(streamId, eventNumber, +1);

		if (lastPrepare != null && _systemStreams.IsMetaStream(streamId)) {
			var rawMeta = lastPrepare.Data;
			_streamRawMetas.Put(_systemStreams.OriginalStreamOf(streamId), new StreamMeta(rawMeta, null), +1);
		}
	}

	public void PreCommit(ReadOnlySpan<IPrepareLogRecord<TStreamId>> commitedPrepares) {
		if (commitedPrepares.Length == 0)
			return;

		var lastPrepare = commitedPrepares[^1];
		var streamId = lastPrepare.EventStreamId;
		long eventNumber = EventNumber.Invalid;
		foreach (var prepare in commitedPrepares) {
			if (prepare.Flags.HasNoneOf(PrepareFlags.StreamDelete | PrepareFlags.Data))
				continue;

			if (!StreamIdComparer.Equals(prepare.EventStreamId, streamId))
				throw new Exception(string.Format("Expected stream: {0}, actual: {1}.", streamId,
					prepare.EventStreamId));

			eventNumber =
				prepare.ExpectedVersion + 1; /* for committed prepare expected version is always explicit */
			_committedEvents.PutRecord(prepare.EventId, new EventInfo(streamId, eventNumber),
				throwOnDuplicate: false);
		}

		_notProcessedCommits.Enqueue(new CommitInfo(streamId, lastPrepare.LogPosition));
		_streamVersions.Put(streamId, eventNumber, 1);
		if (_systemStreams.IsMetaStream(streamId)) {
			var rawMeta = lastPrepare.Data;
			_streamRawMetas.Put(_systemStreams.OriginalStreamOf(streamId), new StreamMeta(rawMeta, null), +1);
		}
	}

	public void UpdateTransactionInfo(long transactionId, long logPosition, TransactionInfo<TStreamId> transactionInfo) {
		_notProcessedTrans.Enqueue(new TransInfo(transactionId, logPosition));
		_transactionInfoCache.Put(transactionId, transactionInfo, +1);
	}

	public TransactionInfo<TStreamId> GetTransactionInfo(long writerCheckpoint, long transactionId) {
		TransactionInfo<TStreamId> transactionInfo;
		if (!_transactionInfoCache.TryGet(transactionId, out transactionInfo)) {
			if (GetTransactionInfoUncached(writerCheckpoint, transactionId, out transactionInfo))
				_transactionInfoCache.Put(transactionId, transactionInfo, 0);
			else
				transactionInfo = new TransactionInfo<TStreamId>(int.MinValue, default);
			Interlocked.Increment(ref _notCachedTransInfo);
		} else {
			Interlocked.Increment(ref _cachedTransInfo);
		}

		return transactionInfo;
	}

	private bool GetTransactionInfoUncached(long writerCheckpoint, long transactionId,
		out TransactionInfo<TStreamId> transactionInfo) {
		using (var reader = _indexBackend.BorrowReader()) {
			reader.Reposition(writerCheckpoint);
			SeqReadResult result;
			while ((result = reader.TryReadPrev()).Success) {
				if (result.LogRecord.LogPosition < transactionId)
					break;
				if (result.LogRecord.RecordType != LogRecordType.Prepare)
					continue;
				var prepare = (IPrepareLogRecord<TStreamId>)result.LogRecord;
				if (prepare.TransactionPosition == transactionId) {
					transactionInfo = new TransactionInfo<TStreamId>(prepare.TransactionOffset, prepare.EventStreamId);
					return true;
				}
			}
		}

		transactionInfo = new TransactionInfo<TStreamId>(int.MinValue, default);
		return false;
	}

	public void PurgeNotProcessedCommitsTill(long checkpoint) {
		while (_notProcessedCommits.Count > 0 && _notProcessedCommits.Peek().LogPosition < checkpoint) {
			var commitInfo = _notProcessedCommits.Dequeue();
			// decrease stickiness
			_streamVersions.Put(
				commitInfo.StreamId,
				x => {
					if (!Debugger.IsAttached)
						Debugger.Launch();
					else
						Debugger.Break();
					throw new Exception(string.Format("CommitInfo for stream '{0}' is not present!", x));
				},
				(streamId, oldVersion) => oldVersion,
				stickiness: -1);
			if (_systemStreams.IsMetaStream(commitInfo.StreamId)) {
				_streamRawMetas.Put(
					_systemStreams.OriginalStreamOf(commitInfo.StreamId),
					x => {
						if (!Debugger.IsAttached)
							Debugger.Launch();
						else
							Debugger.Break();
						throw new Exception(string.Format(
							"Original stream CommitInfo for meta-stream '{0}' is not present!",
							_systemStreams.MetaStreamOf(x)));
					},
					(streamId, oldVersion) => oldVersion,
					stickiness: -1);
			}
		}
	}

	public void PurgeNotProcessedTransactions(long checkpoint) {
		while (_notProcessedTrans.Count > 0 && _notProcessedTrans.Peek().LogPosition < checkpoint) {
			var transInfo = _notProcessedTrans.Dequeue();
			// decrease stickiness
			_transactionInfoCache.Put(
				transInfo.TransactionId,
				x => { throw new Exception(string.Format("TransInfo for transaction ID {0} is not present!", x)); },
				(streamId, oldTransInfo) => oldTransInfo,
				stickiness: -1);
		}
	}

	private IEnumerable<IPrepareLogRecord<TStreamId>> GetTransactionPrepares(long transactionPos, long commitPos) {
		using (var reader = _indexBackend.BorrowReader()) {
			reader.Reposition(transactionPos);

			// in case all prepares were scavenged, we should not read past Commit LogPosition
			SeqReadResult result;
			while ((result = reader.TryReadNext()).Success && result.RecordPrePosition <= commitPos) {
				if (result.LogRecord.RecordType != LogRecordType.Prepare)
					continue;

				var prepare = (IPrepareLogRecord<TStreamId>)result.LogRecord;
				if (prepare.TransactionPosition == transactionPos) {
					yield return prepare;
					if (prepare.Flags.HasAnyOf(PrepareFlags.TransactionEnd))
						yield break;
				}
			}
		}
	}

	public TStreamId GetStreamId(string streamName) {
		return _streamIds.LookupValue(streamName);
	}

	public string GetStreamName(TStreamId streamId) {
		return _streamNames.LookupName(streamId);
	}

	public bool IsSoftDeleted(TStreamId streamId) {
		return GetStreamMetadata(streamId).TruncateBefore == EventNumber.DeletedStream;
	}

	public long GetStreamLastEventNumber(TStreamId streamId) {
		long lastEventNumber;
		if (_streamVersions.TryGet(streamId, out lastEventNumber))
			return lastEventNumber;
		return _indexReader.GetStreamLastEventNumber(streamId);
	}

	public StreamMetadata GetStreamMetadata(TStreamId streamId) {
		StreamMeta meta;
		if (_streamRawMetas.TryGet(streamId, out meta)) {
			if (meta.Meta != null)
				return meta.Meta;
			var m = Helper.EatException(() => StreamMetadata.FromJsonBytes(meta.RawMeta), StreamMetadata.Empty);
			_streamRawMetas.Put(streamId, new StreamMeta(meta.RawMeta, m), 0);
			return m;
		}

		return _indexReader.GetStreamMetadata(streamId);
	}

	public RawMetaInfo GetStreamRawMeta(TStreamId streamId) {
		var metastreamId = _systemStreams.MetaStreamOf(streamId);
		var metaLastEventNumber = GetStreamLastEventNumber(metastreamId);

		StreamMeta meta;
		if (!_streamRawMetas.TryGet(streamId, out meta))
			meta = new StreamMeta(_indexReader.ReadPrepare(metastreamId, metaLastEventNumber).Data, null);

		return new RawMetaInfo(metaLastEventNumber, meta.RawMeta);
	}

	private struct StreamMeta {
		public readonly ReadOnlyMemory<byte> RawMeta;
		public readonly StreamMetadata Meta;

		public StreamMeta(ReadOnlyMemory<byte> rawMeta, StreamMetadata meta) {
			RawMeta = rawMeta;
			Meta = meta;
		}
	}

	private struct EventInfo {
		public readonly TStreamId StreamId;
		public readonly long EventNumber;

		public EventInfo(TStreamId streamId, long eventNumber) {
			StreamId = streamId;
			EventNumber = eventNumber;
		}
	}

	private struct TransInfo {
		public readonly long TransactionId;
		public readonly long LogPosition;

		public TransInfo(long transactionId, long logPosition) {
			TransactionId = transactionId;
			LogPosition = logPosition;
		}
	}

	private struct CommitInfo {
		public readonly TStreamId StreamId;
		public readonly long LogPosition;

		public CommitInfo(TStreamId streamId, long logPosition) {
			StreamId = streamId;
			LogPosition = logPosition;
		}
	}
}
