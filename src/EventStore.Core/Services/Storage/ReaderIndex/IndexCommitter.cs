// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Index;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Messages;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.LogCommon;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Storage.ReaderIndex;

public interface IIndexCommitter {
	long LastIndexedPosition { get; }
	void Init(long buildToPosition);
	void Dispose();
	long Commit(CommitLogRecord commit, bool isTfEof, bool cacheLastEventNumber);
	long GetCommitLastEventNumber(CommitLogRecord commit);
}

public interface IIndexCommitter<TStreamId> : IIndexCommitter {
	long Commit(IList<IPrepareLogRecord<TStreamId>> commitedPrepares, bool isTfEof, bool cacheLastEventNumber);
}

public abstract class IndexCommitter {
	public static readonly ILogger Log = Serilog.Log.ForContext<IndexCommitter>();
}

public class IndexCommitter<TStreamId> : IndexCommitter, IIndexCommitter<TStreamId> {
	private static EqualityComparer<TStreamId> StreamIdComparer { get; } = EqualityComparer<TStreamId>.Default;

	public long LastIndexedPosition => _indexChk.Read();

	private readonly IPublisher _bus;
	private readonly IIndexBackend<TStreamId> _backend;
	private readonly IIndexReader<TStreamId> _indexReader;
	private readonly ITableIndex<TStreamId> _tableIndex;
	private readonly INameIndexConfirmer<TStreamId> _streamNameIndex;
	private readonly INameLookup<TStreamId> _streamNames;
	private readonly INameIndexConfirmer<TStreamId> _eventTypeIndex;
	private readonly INameLookup<TStreamId> _eventTypes;
	private readonly ISystemStreamLookup<TStreamId> _systemStreams;
	private readonly INameExistenceFilter _streamExistenceFilter;
	private readonly IIndexStatusTracker _statusTracker;
	private readonly IIndexTracker _tracker;
	private INameExistenceFilterInitializer _streamExistenceFilterInitializer;
	private readonly bool _additionalCommitChecks;
	private long _persistedPreparePos = -1;
	private long _persistedCommitPos = -1;
	private bool _indexRebuild = true;
	private readonly ICheckpoint _indexChk;

	public IndexCommitter(
		IPublisher bus,
		IIndexBackend<TStreamId> backend,
		IIndexReader<TStreamId> indexReader,
		ITableIndex<TStreamId> tableIndex,
		INameIndexConfirmer<TStreamId> streamNameIndex,
		INameLookup<TStreamId> streamNames,
		INameIndexConfirmer<TStreamId> eventTypeIndex,
		INameLookup<TStreamId> eventTypes,
		ISystemStreamLookup<TStreamId> systemStreams,
		INameExistenceFilter streamExistenceFilter,
		INameExistenceFilterInitializer streamExistenceFilterInitializer,
		ICheckpoint indexChk,
		IIndexStatusTracker statusTracker,
		IIndexTracker tracker,
		bool additionalCommitChecks) {
		_bus = bus;
		_backend = backend;
		_indexReader = indexReader;
		_tableIndex = tableIndex;
		_streamNameIndex = streamNameIndex;
		_streamNames = streamNames;
		_eventTypeIndex = eventTypeIndex;
		_eventTypes = eventTypes;
		_systemStreams = systemStreams;
		_streamExistenceFilter = streamExistenceFilter;
		_streamExistenceFilterInitializer = streamExistenceFilterInitializer;
		_indexChk = indexChk;
		_additionalCommitChecks = additionalCommitChecks;
		_statusTracker = statusTracker;
		_tracker = tracker;
	}

	public void Init(long buildToPosition) {
		Log.Information("TableIndex initialization...");

		using (_statusTracker.StartOpening()) {
			_tableIndex.Initialize(buildToPosition);
		}

		_persistedPreparePos = _tableIndex.PrepareCheckpoint;
		_persistedCommitPos = _tableIndex.CommitCheckpoint;
		//todo(clc) determin if this needs to move into the TableIndex re:project-io
		_indexChk.Write(_tableIndex.CommitCheckpoint);
		_indexChk.Flush();

		if (_indexChk.Read() >= buildToPosition)
			throw new Exception(string.Format("_lastCommitPosition {0} >= buildToPosition {1}", _indexChk.Read(),
				buildToPosition));

		var startTime = DateTime.UtcNow;
		var lastTime = DateTime.UtcNow;
		var reportPeriod = TimeSpan.FromSeconds(5);

		Log.Information("ReadIndex building...");

		// V2 index:
		// right now its possible for entries to get into the main index before being replicated
		// (because we catch up to the chaser position)
		// when we join the cluster it may turn out that some of what we indexed needs truncating.
		// this is dealt with by adjusting the checkpoints and restarting. usually
		// the unwanted index entries were only in memory so restarting will discard them.
		// if they do happen to have been persisted, the above will delete the whole index.
		//
		// V3 index:
		// the upshot for the stream name index is that here we must initialise the stream
		// name index with the main index before we catch it up, even though it will likely
		// mean entries need to be removed only to be readded.
		// 
		// after we only allow replicated entries into the index we can be sure that
		// neither index will need truncating and this will become more elegant.
		_streamNameIndex.InitializeWithConfirmed(_streamNames);
		_eventTypeIndex.InitializeWithConfirmed(_eventTypes);

		_indexRebuild = true;
		using (_statusTracker.StartRebuilding())
		using (var reader = _backend.BorrowReader()) {
			var startPosition = Math.Max(0, _persistedCommitPos);
			var fullRebuild = startPosition == 0;
			reader.Reposition(startPosition);

			var commitedPrepares = new List<IPrepareLogRecord<TStreamId>>();

			long processed = 0;
			SeqReadResult result;
			while ((result = reader.TryReadNext()).Success && result.LogRecord.LogPosition < buildToPosition) {
				switch (result.LogRecord.RecordType) {
					case LogRecordType.Stream:
					case LogRecordType.EventType:
					case LogRecordType.Prepare: {
						var prepare = (IPrepareLogRecord<TStreamId>)result.LogRecord;
						if (prepare.Flags.HasAnyOf(PrepareFlags.IsCommitted)) {
							if (prepare.Flags.HasAnyOf(PrepareFlags.SingleWrite)) {
								Commit(commitedPrepares, false, false);
								commitedPrepares.Clear();
								Commit(new[] { prepare }, result.Eof, false);
							} else {
								if (prepare.Flags.HasAnyOf(PrepareFlags.Data | PrepareFlags.StreamDelete))
									commitedPrepares.Add(prepare);
								if (prepare.Flags.HasAnyOf(PrepareFlags.TransactionEnd)) {
									Commit(commitedPrepares, result.Eof, false);
									commitedPrepares.Clear();
								}
							}
						}

						break;
					}
					case LogRecordType.Commit:
						Commit((CommitLogRecord)result.LogRecord, result.Eof, false);
						break;
					case LogRecordType.System:
					case LogRecordType.Partition:
					case LogRecordType.PartitionType:
						break;
					default:
						throw new Exception(string.Format("Unknown RecordType: {0}", result.LogRecord.RecordType));
				}

				processed += 1;
				if (DateTime.UtcNow - lastTime > reportPeriod || processed % 100000 == 0) {
					Log.Debug("ReadIndex Rebuilding: processed {processed} records ({resultPosition:0.0}%).",
						processed,
						(result.RecordPostPosition - startPosition) * 100.0 / (buildToPosition - startPosition));
					lastTime = DateTime.UtcNow;
				}

				if (fullRebuild && processed % 1000000 == 0) {
					if (_tableIndex.IsBackgroundTaskRunning) {
						Log.Debug("Pausing ReadIndex Rebuild due to ongoing index merges.");
						while (_tableIndex.IsBackgroundTaskRunning) {
							Thread.Sleep(1000);
						}
						Log.Debug("Resuming ReadIndex Rebuild.");
					}
				}
			}

			Log.Debug("ReadIndex rebuilding done: total processed {processed} records, time elapsed: {elapsed}.",
				processed, DateTime.UtcNow - startTime);
			startTime = DateTime.UtcNow;

			// now that the main index has caught up, we initialize the stream existence filter to add any missing entries.
			// while the index above is concerned with building exactly to the buildToPosition and not beyond, that isn't
			// important for the streamexistencefiler since false positives are allowed. it only cares about truncating back
			// to the buildToPosition (if necessary).
			// V2:
			// reads the index and transaction file forward from the last checkpoint (a log position) and adds stream names to the filter, possibly multiple times
			// but it's not an issue since it's idempotent
			//
			// V3:
			// reads the stream created stream forward from the last checkpoint (a stream number) and adds stream names to the filter
			//
			// V2/V3 note: it's possible that we add extra uncommitted entries to the filter if the index or log later gets truncated when joining
			// the cluster but false positives are not a problem since it's a probabilistic filter
			Log.Debug("Initializing the StreamExistenceFilter. The filter can be disabled by setting the configuration option \"StreamExistenceFilterSize\" to 0");
			_statusTracker.StartInitializing();
			_streamExistenceFilter.Initialize(_streamExistenceFilterInitializer, truncateToPosition: buildToPosition);
			Log.Debug("StreamExistenceFilter initialized. Time elapsed: {elapsed}.",
				DateTime.UtcNow - startTime);

			_bus.Publish(new StorageMessage.TfEofAtNonCommitRecord());
			_backend.SetSystemSettings(GetSystemSettings());
		}
		_indexRebuild = false;
	}

	public void Dispose() {
		_streamNameIndex?.Dispose();
		_eventTypeIndex?.Dispose();
		_streamExistenceFilter?.Dispose();
		try {
			_tableIndex.Close(removeFiles: false);
		} catch (TimeoutException exc) {
			Log.Error(exc, "Timeout exception when trying to close TableIndex.");
			throw;
		}
	}

	public long GetCommitLastEventNumber(CommitLogRecord commit) {
		long eventNumber = EventNumber.Invalid;

		var lastIndexedPosition = _indexChk.Read();
		if (commit.LogPosition < lastIndexedPosition || (commit.LogPosition == lastIndexedPosition && !_indexRebuild))
			return eventNumber;

		foreach (var prepare in GetTransactionPrepares(commit.TransactionPosition, commit.LogPosition)) {
			if (prepare.Flags.HasNoneOf(PrepareFlags.StreamDelete | PrepareFlags.Data))
				continue;
			eventNumber = prepare.Flags.HasAllOf(PrepareFlags.StreamDelete)
				? EventNumber.DeletedStream
				: commit.FirstEventNumber + prepare.TransactionOffset;
		}

		return eventNumber;
	}

	public long Commit(CommitLogRecord commit, bool isTfEof, bool cacheLastEventNumber) {
		long eventNumber = EventNumber.Invalid;

		var lastIndexedPosition = _indexChk.Read();
		if (commit.LogPosition < lastIndexedPosition || (commit.LogPosition == lastIndexedPosition && !_indexRebuild))
			return eventNumber; // already committed

		TStreamId streamId = default;
		var indexEntries = new List<IndexKey<TStreamId>>();
		var prepares = new List<IPrepareLogRecord<TStreamId>>();

		foreach (var prepare in GetTransactionPrepares(commit.TransactionPosition, commit.LogPosition)) {
			if (prepare.Flags.HasNoneOf(PrepareFlags.StreamDelete | PrepareFlags.Data))
				continue;

			if (StreamIdComparer.Equals(streamId, default)) {
				streamId = prepare.EventStreamId;
			} else {
				if (!StreamIdComparer.Equals(prepare.EventStreamId, streamId))
					throw new Exception(string.Format("Expected stream: {0}, actual: {1}. LogPosition: {2}",
						streamId, prepare.EventStreamId, commit.LogPosition));
			}

			eventNumber = prepare.Flags.HasAllOf(PrepareFlags.StreamDelete)
				? EventNumber.DeletedStream
				: commit.FirstEventNumber + prepare.TransactionOffset;

			if (new TFPos(commit.LogPosition, prepare.LogPosition) >
				new TFPos(_persistedCommitPos, _persistedPreparePos)) {
				indexEntries.Add(new IndexKey<TStreamId>(streamId, eventNumber, prepare.LogPosition));
				prepares.Add(prepare);
			}
		}

		if (indexEntries.Count > 0) {
			if (_additionalCommitChecks && cacheLastEventNumber) {
				CheckStreamVersion(streamId, indexEntries[0].Version, commit);
				CheckDuplicateEvents(streamId, commit, indexEntries, prepares);
			}

			_tableIndex.AddEntries(commit.LogPosition, indexEntries); // atomically add a whole bulk of entries
		}

		if (eventNumber != EventNumber.Invalid) {
			if (eventNumber < 0)
				throw new Exception(string.Format("EventNumber {0} is incorrect.", eventNumber));

			if (cacheLastEventNumber) {
				_backend.SetStreamLastEventNumber(streamId, eventNumber);
			}

			if (_systemStreams.IsMetaStream(streamId))
				_backend.SetStreamMetadata(_systemStreams.OriginalStreamOf(streamId),
					null); // invalidate cached metadata

			if (StreamIdComparer.Equals(streamId, _systemStreams.SettingsStream))
				_backend.SetSystemSettings(DeserializeSystemSettings(prepares[prepares.Count - 1].Data));
		}

		// todo: refactor into one call
		_streamNameIndex.Confirm(prepares, commit, _indexRebuild, _backend);
		_eventTypeIndex.Confirm(prepares, commit, _indexRebuild, _backend);

		var newLastIndexedPosition = Math.Max(commit.LogPosition, lastIndexedPosition);
		if (_indexChk.Read() != lastIndexedPosition) {
			throw new Exception(
				"Concurrency error in ReadIndex.Commit: _lastCommitPosition was modified during Commit execution!");
		}
		_indexChk.Write(newLastIndexedPosition);
		_indexChk.Flush();

		if (!_indexRebuild) {
			var streamName = _streamNames.LookupName(streamId);
			for (int i = 0, n = indexEntries.Count; i < n; ++i) {
				var eventType = _eventTypes.LookupName(prepares[i].EventType);
				_bus.Publish(
					new StorageMessage.EventCommitted(
						commit.LogPosition,
						new EventRecord(indexEntries[i].Version, prepares[i], streamName, eventType),
						isTfEof && i == n - 1));
			}
		}

		return eventNumber;
	}

	public long Commit(IList<IPrepareLogRecord<TStreamId>> commitedPrepares, bool isTfEof, bool cacheLastEventNumber) {
		long eventNumber = EventNumber.Invalid;

		if (commitedPrepares.Count == 0)
			return eventNumber;

		var lastIndexedPosition = _indexChk.Read();
		var lastPrepare = commitedPrepares[commitedPrepares.Count - 1];

		var streamId = lastPrepare.EventStreamId;
		var indexEntries = new List<IndexKey<TStreamId>>();
		var prepares = new List<IPrepareLogRecord<TStreamId>>();

		foreach (var prepare in commitedPrepares) {
			if (prepare.Flags.HasNoneOf(PrepareFlags.StreamDelete | PrepareFlags.Data))
				continue;

			if (!StreamIdComparer.Equals(prepare.EventStreamId, streamId)) {
				var sb = new StringBuilder();
				sb.Append(string.Format("ERROR: Expected stream: {0}, actual: {1}.", streamId,
					prepare.EventStreamId));
				sb.Append(Environment.NewLine);
				sb.Append(Environment.NewLine);
				sb.Append("Prepares: (" + commitedPrepares.Count + ")");
				sb.Append(Environment.NewLine);
				for (int i = 0; i < commitedPrepares.Count; i++) {
					var p = commitedPrepares[i];
					sb.Append("Stream ID: " + p.EventStreamId);
					sb.Append(Environment.NewLine);
					sb.Append("LogPosition: " + p.LogPosition);
					sb.Append(Environment.NewLine);
					sb.Append("Flags: " + p.Flags);
					sb.Append(Environment.NewLine);
					sb.Append("Type: " + p.EventType);
					sb.Append(Environment.NewLine);
					sb.Append("MetaData: " + Encoding.UTF8.GetString(p.Metadata.Span));
					sb.Append(Environment.NewLine);
					sb.Append("Data: " + Encoding.UTF8.GetString(p.Data.Span));
					sb.Append(Environment.NewLine);
				}

				throw new Exception(sb.ToString());
			}

			if (prepare.LogPosition < lastIndexedPosition ||
				(prepare.LogPosition == lastIndexedPosition && !_indexRebuild))
				continue; // already committed

			eventNumber =
				prepare.ExpectedVersion + 1; /* for committed prepare expected version is always explicit */

			if (new TFPos(prepare.LogPosition, prepare.LogPosition) >
				new TFPos(_persistedCommitPos, _persistedPreparePos)) {
				indexEntries.Add(new IndexKey<TStreamId>(streamId, eventNumber, prepare.LogPosition));
				prepares.Add(prepare);
			}
		}

		if (indexEntries.Count > 0) {
			if (_additionalCommitChecks && cacheLastEventNumber) {
				CheckStreamVersion(streamId, indexEntries[0].Version, null); // TODO AN: bad passing null commit
				CheckDuplicateEvents(streamId, null, indexEntries, prepares); // TODO AN: bad passing null commit
			}

			_tableIndex.AddEntries(lastPrepare.LogPosition, indexEntries); // atomically add a whole bulk of entries
		}

		if (eventNumber != EventNumber.Invalid) {
			if (eventNumber < 0)
				throw new Exception(string.Format("EventNumber {0} is incorrect.", eventNumber));

			if (cacheLastEventNumber) {
				_backend.SetStreamLastEventNumber(streamId, eventNumber);
			}

			if (_systemStreams.IsMetaStream(streamId))
				_backend.SetStreamMetadata(_systemStreams.OriginalStreamOf(streamId),
					null); // invalidate cached metadata

			if (StreamIdComparer.Equals(streamId, _systemStreams.SettingsStream))
				_backend.SetSystemSettings(DeserializeSystemSettings(prepares[prepares.Count - 1].Data));
		}

		_streamNameIndex.Confirm(prepares, _indexRebuild, _backend);
		_eventTypeIndex.Confirm(prepares, _indexRebuild, _backend);

		var newLastIndexedPosition = Math.Max(lastPrepare.LogPosition, lastIndexedPosition);
		if (_indexChk.Read() != lastIndexedPosition) {
			throw new Exception(
				"Concurrency error in ReadIndex.Commit: _lastCommitPosition was modified during Commit execution!");
		}
		_indexChk.Write(newLastIndexedPosition);
		_indexChk.Flush();

		if (!_indexRebuild) {
			var streamName = _streamNames.LookupName(streamId);
			for (int i = 0, n = indexEntries.Count; i < n; ++i) {
				var eventType = _eventTypes.LookupName(prepares[i].EventType);
				_bus.Publish(
					new StorageMessage.EventCommitted(
						prepares[i].LogPosition,
						new EventRecord(indexEntries[i].Version, prepares[i], streamName, eventType),
						isTfEof && i == n - 1));
			}

			_tracker.OnIndexed(prepares);
		}

		return eventNumber;
	}

	private IEnumerable<IPrepareLogRecord<TStreamId>> GetTransactionPrepares(long transactionPos, long commitPos) {
		using (var reader = _backend.BorrowReader()) {
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

	private void CheckStreamVersion(TStreamId streamId, long newEventNumber, CommitLogRecord commit) {
		if (newEventNumber == EventNumber.DeletedStream)
			return;

		long lastEventNumber = _indexReader.GetStreamLastEventNumber(streamId);
		if (newEventNumber != lastEventNumber + 1) {
			if (Debugger.IsAttached)
				Debugger.Break();
			else
				throw new Exception(
					string.Format(
						"Commit invariant violation: new event number {0} does not correspond to current stream version {1}.\n"
						+ "Stream ID: {2}.\nCommit: {3}.", newEventNumber, lastEventNumber, streamId, commit));
		}
	}

	private void CheckDuplicateEvents(TStreamId streamId, CommitLogRecord commit, IList<IndexKey<TStreamId>> indexEntries,
		IList<IPrepareLogRecord<TStreamId>> prepares) {
		using (var reader = _backend.BorrowReader()) {
			var entries = _tableIndex.GetRange(streamId, indexEntries[0].Version,
				indexEntries[indexEntries.Count - 1].Version);
			foreach (var indexEntry in entries) {
				int prepareIndex = (int)(indexEntry.Version - indexEntries[0].Version);
				var prepare = prepares[prepareIndex];
				IPrepareLogRecord<TStreamId> indexedPrepare = GetPrepare(reader, indexEntry.Position);
				if (indexedPrepare != null && StreamIdComparer.Equals(indexedPrepare.EventStreamId, prepare.EventStreamId)) {
					if (Debugger.IsAttached)
						Debugger.Break();
					else
						throw new Exception(
							string.Format("Trying to add duplicate event #{0} to stream {1} \nCommit: {2}\n"
										  + "Prepare: {3}\nIndexed prepare: {4}.",
								indexEntry.Version, prepare.EventStreamId, commit, prepare, indexedPrepare));
				}
			}
		}
	}

	private SystemSettings GetSystemSettings() {
		var res = _indexReader.ReadEvent(IndexReader.UnspecifiedStreamName, _systemStreams.SettingsStream, -1);
		return res.Result == ReadEventResult.Success ? DeserializeSystemSettings(res.Record.Data) : null;
	}

	private static SystemSettings DeserializeSystemSettings(ReadOnlyMemory<byte> settingsData) {
		try {
			return SystemSettings.FromJsonBytes(settingsData);
		} catch (Exception exc) {
			Log.Error(exc, "Error deserializing SystemSettings record.");
		}

		return null;
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
}
