// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.Services;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.TransactionLog.Chunks.TFChunk;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.LogCommon;

namespace KurrentDB.Core.Tests.TransactionLog.Scavenging.Helpers;

public class TFChunkDbCreationHelper<TLogFormat, TStreamId> {
	private static EqualityComparer<TStreamId> StreamIdComparer { get; } = EqualityComparer<TStreamId>.Default;

	private readonly TFChunkDbConfig _dbConfig;
	private readonly TFChunkDb _db;

	private readonly List<Rec[]> _chunkRecs = [];
	private readonly HashSet<int> _remoteChunks = [];

	private bool _completeLast;

	private readonly LogFormatAbstractor<TStreamId> _logFormat;
	private readonly TStreamId _scavengePointEventTypeId;

	private TFChunkDbCreationHelper(TFChunkDbConfig dbConfig, LogFormatAbstractor<TStreamId> logFormat,
		IChunkFileSystem fileSystem = null) {

		Ensure.NotNull(dbConfig, "dbConfig");

		_dbConfig = dbConfig;
		_logFormat = logFormat;
		_scavengePointEventTypeId = logFormat.EventTypeIndex.GetExisting(SystemEventTypes.ScavengePoint);

		_db = new TFChunkDb(_dbConfig, fileSystem: fileSystem);
	}

	public static async ValueTask<TFChunkDbCreationHelper<TLogFormat, TStreamId>> CreateAsync(
		TFChunkDbConfig dbConfig,
		LogFormatAbstractor<TStreamId> logFormat,
		IChunkFileSystem fileSystem = null,
		CancellationToken token = default) {

		var result = new TFChunkDbCreationHelper<TLogFormat, TStreamId>(dbConfig, logFormat, fileSystem);

		await result._db.Open(token: token);

		if (result._db.Config.WriterCheckpoint.ReadNonFlushed() > 0)
			throw new Exception("The DB already contains some data.");

		return result;
	}

	public TFChunkDbCreationHelper<TLogFormat, TStreamId> Chunk(params Rec[] records) {
		_chunkRecs.Add(records);
		return this;
	}

	public TFChunkDbCreationHelper<TLogFormat, TStreamId> RemoteChunk(params Rec[] records) {
		_remoteChunks.Add(_chunkRecs.Count);
		_chunkRecs.Add(records);
		return this;
	}

	public TFChunkDbCreationHelper<TLogFormat, TStreamId> CompleteLastChunk() {
		_completeLast = true;
		return this;
	}

	public async ValueTask<DbResult> CreateDb(bool commit = false, CancellationToken token = default) {
		var records = new List<ILogRecord>[_chunkRecs.Count];
		for (int i = 0; i < records.Length; ++i) {
			records[i] = new();
		}

		var transactions = new Dictionary<int, TransactionInfo>();
		var streams = new Dictionary<string, StreamInfo>();
		var streamUncommittedVersion = new Dictionary<string, long>();

		// for each chunk i
		for (int i = 0; i < _chunkRecs.Count; ++i) {
			// for each record j in chunk i
			for (int j = 0; j < _chunkRecs[i].Length; ++j) {
				var rec = _chunkRecs[i][j];

				TransactionInfo transInfo;
				bool transCreate = transactions.TryGetValue(rec.Transaction, out transInfo);
				if (!transCreate) {
					if (rec.Type == Rec.RecType.Commit)
						throw new Exception("Commit for non-existing transaction.");

					transactions[rec.Transaction] = transInfo = new TransactionInfo(rec.StreamId, rec.Id, rec.Id);

					streams[rec.StreamId] = new StreamInfo(-1);
					streamUncommittedVersion[rec.StreamId] = -1;
				} else {
					if (rec.Type == Rec.RecType.TransStart)
						throw new Exception(string.Format("Unexpected record type: {0}.", rec.Type));
				}

				if (transInfo.StreamId != rec.StreamId) {
					throw new Exception(string.Format(
						"Wrong stream id for transaction. Transaction StreamId: {0}, record StreamId: {1}.",
						transInfo.StreamId,
						rec.StreamId));
				}

				if (rec.Type != Rec.RecType.Commit && transInfo.IsDelete)
					throw new Exception("Transaction with records after delete record.");

				if (rec.Type == Rec.RecType.Delete)
					transInfo.IsDelete = true;

				transInfo.LastPrepareId = rec.Id;
			}
		}

		long logPos = 0;

		// convert the Recs into LogRecords and write them to the database.
		// for each chunk i
		for (int i = 0; i < _chunkRecs.Count; ++i) {
			var chunk = await (i is 0
				? _db.Manager.GetInitializedChunk(0, token)
				: _db.Manager.AddNewChunk(token));
			logPos = i * (long)_db.Config.ChunkSize;

			var completedChunk = false;
			// for each record j in chunk i
			for (int j = 0; j < _chunkRecs[i].Length; ++j) {
				if (completedChunk)
					throw new InvalidOperationException("Don't try to write more data to completed chunk");
				var rec = _chunkRecs[i][j];
				var transInfo = transactions[rec.Transaction];

				_logFormat.StreamNameIndex.GetOrReserve(_logFormat.RecordFactory, rec.StreamId, logPos, out var streamNumber, out var streamRecord);
				if (streamRecord != null) {
					logPos = await Write(i, chunk, streamRecord, false, token);
					records[i].Add(streamRecord);
				}

				_logFormat.EventTypeIndex.GetOrReserveEventType(_logFormat.RecordFactory, rec.EventType, logPos, out var eventTypeNumber, out var eventTypeRecord);
				if (eventTypeRecord != null) {
					logPos = await Write(i, chunk, eventTypeRecord, false, token);
					records[i].Add(eventTypeRecord);
				}

				long streamVersion = streamUncommittedVersion[rec.StreamId];

				if (rec.EventNumber.HasValue && rec.EventNumber > streamVersion + 1) {
					// advance the stream
					streamVersion = rec.EventNumber.Value - 1;
					streamUncommittedVersion[rec.StreamId] = streamVersion;
				}

				if (streamVersion == -1
					&& rec.Type != Rec.RecType.TransStart
					&& rec.Type != Rec.RecType.Prepare
					&& rec.Type != Rec.RecType.Delete) {
					throw new Exception(string.Format("Stream {0} is empty.", rec.StreamId));
				}

				if (streamVersion == EventNumber.DeletedStream && rec.Type != Rec.RecType.Commit)
					throw new Exception(string.Format(
						"Stream {0} was deleted, but we need to write some more prepares.", rec.StreamId));

				if (transInfo.FirstPrepareId == rec.Id) {
					transInfo.TransactionPosition = logPos;
					transInfo.TransactionEventNumber = streamVersion + 1;
					transInfo.TransactionOffset = 0;
				}

				ILogRecord record;

				var logFormatDefaultExpectedVersion = LogFormatHelper<TLogFormat, TStreamId>.IsV2
					? ExpectedVersion.Any // V2 transaction writes have expected version: -2, event number: -1
					: ExpectedVersion.NoStream; // V3 doesn't have transactions so cant have a negative event number

				var expectedVersion =
				  (rec.EventNumber - 1) ??
				  (transInfo.FirstPrepareId == rec.Id ? streamVersion : logFormatDefaultExpectedVersion);

				switch (rec.Type) {
					case Rec.RecType.Prepare: {
						record = CreateLogRecord(rec, streamNumber, eventTypeNumber, transInfo, logPos, expectedVersion);

						if (SystemStreams.IsMetastream(rec.StreamId))
							transInfo.StreamMetadata = rec.Metadata;

						streamUncommittedVersion[rec.StreamId] += 1;
						break;
					}

					case Rec.RecType.Delete: {
						if (rec.PrepareFlags.HasAnyOf(PrepareFlags.IsCommitted)) {
							// modern style tombstone has IsCommitted flag and max event number.
							// introduced in 19f7cd351329f7bd2be0f6c00b4beee45e2a4777
							expectedVersion = rec.Version == LogRecordVersion.LogRecordV0
								? int.MaxValue - 1
								: EventNumber.DeletedStream - 1;
						} else {
							// legacy style tombstone has 'normal' expected version in the log record
							// (probably -1 because it is committed separately)
							// a subsequent commit record to commit it
							// special handling in indexcommitter to give it a max event number in
							// the index.
						}

						record = CreateLogRecord(rec, streamNumber, eventTypeNumber, transInfo, logPos, expectedVersion);

						streamUncommittedVersion[rec.StreamId] = rec.Version == LogRecordVersion.LogRecordV0
							? int.MaxValue
							: EventNumber.DeletedStream;
						break;
					}

					case Rec.RecType.TransStart:
					case Rec.RecType.TransEnd: {
						record = CreateLogRecord(rec, streamNumber, eventTypeNumber, transInfo, logPos, expectedVersion);
						break;
					}
					case Rec.RecType.Commit: {
						record = CreateLogRecord(rec, streamNumber, eventTypeNumber, transInfo, logPos, expectedVersion);

						if (transInfo.StreamMetadata != null) {
							var streamId = SystemStreams.OriginalStreamOf(rec.StreamId);
							if (!streams.ContainsKey(streamId))
								streams.Add(streamId, new StreamInfo(-1));
							streams[streamId].StreamMetadata = transInfo.StreamMetadata;
						}

						if (transInfo.IsDelete)
							streams[rec.StreamId].StreamVersion = EventNumber.DeletedStream;
						else
							streams[rec.StreamId].StreamVersion =
								transInfo.TransactionEventNumber + transInfo.TransactionOffset - 1;
						break;
					}
					default:
						throw new ArgumentOutOfRangeException();
				}

				logPos = await Write(i, chunk, record, rec.CommitWrite, token);
				records[i].Add(record);
				if (record is IPrepareLogRecord<TStreamId> prepare &&
					StreamIdComparer.Equals(prepare.EventType, _scavengePointEventTypeId)) {
					await chunk.Complete(token);
					completedChunk = true;
				}
			}

			if (!completedChunk) {
				if (i < _chunkRecs.Count - 1 || (_completeLast && i == _chunkRecs.Count - 1)) {
					await chunk.Complete(token);
				} else {
					await chunk.Flush(token);
				}
			}
		}

		if (commit)
			_db.Config.WriterCheckpoint.Write(logPos);

		_db.Config.WriterCheckpoint.Flush();
		return new DbResult(_db, records.Select(rs => rs.ToArray()).ToArray(), _remoteChunks, streams);
	}

	async ValueTask<long> Write(int chunkNum, TFChunk chunk, ILogRecord record, bool commitWrite, CancellationToken token) {
		var writerRes = await chunk.TryAppend(record, token);
		if (!writerRes.Success)
			throw new Exception(string.Format("Could not write log record: {0}", record));

		var newPos = chunkNum * (long)_db.Config.ChunkSize + writerRes.NewPosition;

		if (commitWrite)
			_db.Config.WriterCheckpoint.Write(newPos);

		return newPos;
	}

	private byte[] FormatData(Rec rec) {
		return rec.Data ?? rec.Metadata?.ToJsonBytes() ?? rec.Id.ToByteArray();
	}

	private ILogRecord CreateLogRecord(Rec rec, TStreamId streamId, TStreamId eventTypeId, TransactionInfo transInfo, long logPos, long expectedVersion) {
		switch (rec.Type) {
			case Rec.RecType.Prepare: {
				int transOffset = transInfo.TransactionOffset;
				transInfo.TransactionOffset += 1;

				if (rec.Version == LogRecordVersion.LogRecordV0) {
					return CreateLogRecordV0(rec, transInfo, transOffset, logPos, expectedVersion,
						FormatData(rec),
						rec.PrepareFlags
						| PrepareFlags.Data
						| (transInfo.FirstPrepareId == rec.Id ? PrepareFlags.TransactionBegin : PrepareFlags.None)
						| (transInfo.LastPrepareId == rec.Id ? PrepareFlags.TransactionEnd : PrepareFlags.None)
						| (rec.Metadata == null ? PrepareFlags.None : PrepareFlags.IsJson));
				}

				return LogRecord.Prepare(_logFormat.RecordFactory, logPos,
					Guid.NewGuid(),
					rec.Id,
					transInfo.TransactionPosition,
					transOffset,
					streamId,
					expectedVersion,
					rec.PrepareFlags
					| (transInfo.FirstPrepareId == rec.Id ? PrepareFlags.TransactionBegin : PrepareFlags.None)
					| (transInfo.LastPrepareId == rec.Id ? PrepareFlags.TransactionEnd : PrepareFlags.None)
					| (rec.Metadata == null ? PrepareFlags.None : PrepareFlags.IsJson),
					eventTypeId,
					FormatData(rec),
					null,
					properties: rec.Properties,
					rec.TimeStamp);
			}

			case Rec.RecType.Delete: {
				int transOffset = transInfo.TransactionOffset;
				transInfo.TransactionOffset += 1;

				if (rec.Version == LogRecordVersion.LogRecordV0) {
					return CreateLogRecordV0(rec, transInfo, transOffset, logPos, expectedVersion,
						LogRecord.NoData,
						rec.PrepareFlags
						| PrepareFlags.StreamDelete
						| (transInfo.FirstPrepareId == rec.Id ? PrepareFlags.TransactionBegin : PrepareFlags.None)
						| (transInfo.LastPrepareId == rec.Id ? PrepareFlags.TransactionEnd : PrepareFlags.None));
				}

				return LogRecord.Prepare(_logFormat.RecordFactory, logPos,
					Guid.NewGuid(),
					rec.Id,
					transInfo.TransactionPosition,
					transOffset,
					streamId,
					expectedVersion,
					rec.PrepareFlags
					| PrepareFlags.StreamDelete
					| (transInfo.FirstPrepareId == rec.Id ? PrepareFlags.TransactionBegin : PrepareFlags.None)
					| (transInfo.LastPrepareId == rec.Id ? PrepareFlags.TransactionEnd : PrepareFlags.None),
					eventTypeId,
					LogRecord.NoData,
					null,
					rec.TimeStamp);
			}

			case Rec.RecType.TransStart:
			case Rec.RecType.TransEnd: {
				if (rec.Version == LogRecordVersion.LogRecordV0) {
					return CreateLogRecordV0(rec, transInfo, -1, logPos, expectedVersion,
						LogRecord.NoData,
						(transInfo.FirstPrepareId == rec.Id ? PrepareFlags.TransactionBegin : PrepareFlags.None)
						| (transInfo.LastPrepareId == rec.Id ? PrepareFlags.TransactionEnd : PrepareFlags.None));
				}

				return LogRecord.Prepare(_logFormat.RecordFactory, logPos,
					Guid.NewGuid(),
					rec.Id,
					transInfo.TransactionPosition,
					-1,
					streamId,
					expectedVersion,
					(transInfo.FirstPrepareId == rec.Id ? PrepareFlags.TransactionBegin : PrepareFlags.None)
					| (transInfo.LastPrepareId == rec.Id ? PrepareFlags.TransactionEnd : PrepareFlags.None),
					eventTypeId,
					LogRecord.NoData,
					null,
					rec.TimeStamp);
			}
			case Rec.RecType.Commit: {
				if (rec.Version == LogRecordVersion.LogRecordV0) {
					return new CommitLogRecord(logPos, Guid.NewGuid(), transInfo.TransactionPosition,
						DateTime.UtcNow, transInfo.TransactionEventNumber, LogRecordVersion.LogRecordV0);
				}

				return LogRecord.Commit(logPos, Guid.NewGuid(), transInfo.TransactionPosition,
					transInfo.TransactionEventNumber);
			}
			default:
				throw new ArgumentOutOfRangeException();
		}
	}

	private LogRecord CreateLogRecordV0(Rec rec, TransactionInfo transInfo, int transOffset, long logPos,
		long expectedVersion, ReadOnlyMemory<byte> data, PrepareFlags flags) {

		LogFormatHelper<TLogFormat, TStreamId>.EnsureV0PrepareSupported();

		return new PrepareLogRecord(logPos,
			Guid.NewGuid(),
			rec.Id,
			transInfo.TransactionPosition,
			transOffset,
			rec.StreamId,
			null,
			expectedVersion,
			rec.TimeStamp,
			flags,
			rec.EventType,
			null,
			data,
			null,
			null,
			LogRecordVersion.LogRecordV0);
	}
}

public class TransactionInfo {
	public readonly string StreamId;
	public readonly Guid FirstPrepareId;
	public Guid LastPrepareId;
	public long TransactionPosition = -1;
	public int TransactionOffset = 0;
	public long TransactionEventNumber = -1;
	public bool IsDelete = false;
	public StreamMetadata StreamMetadata;

	public TransactionInfo(string streamId, Guid firstPrepareId, Guid lastPrepareId) {
		StreamId = streamId;
		FirstPrepareId = firstPrepareId;
		LastPrepareId = lastPrepareId;
	}
}

public class StreamInfo {
	public long StreamVersion;
	public StreamMetadata StreamMetadata;

	public StreamInfo(long streamVersion) {
		StreamVersion = streamVersion;
	}
}

public sealed class DbResult : IAsyncDisposable {
	public TFChunkDb Db { get; }
	public ILogRecord[][] Recs { get; }
	public HashSet<int> RemoteChunks { get; }
	public Dictionary<string, StreamInfo> Streams { get; }

	public DbResult(TFChunkDb db, ILogRecord[][] recs,
		HashSet<int> remoteChunks,
		Dictionary<string, StreamInfo> streams) {

		Ensure.NotNull(db, "db");
		Ensure.NotNull(recs, "recs");
		Ensure.NotNull(remoteChunks, nameof(remoteChunks));
		Ensure.NotNull(streams, "streams");

		Db = db;
		Recs = recs;
		RemoteChunks = remoteChunks;
		Streams = streams;
	}

	public async ValueTask DisposeAsync() {
		await Db.DisposeAsync();
	}
}

public class Rec {
	public enum RecType {
		Prepare,
		Delete,
		TransStart,
		TransEnd,
		Commit
	}

	public readonly RecType Type;
	public readonly Guid Id;
	public readonly int Transaction;
	public readonly string StreamId;
	public readonly string EventType;
	public readonly DateTime TimeStamp;
	public readonly long? EventNumber;
	public readonly byte[] Data;
	public readonly StreamMetadata Metadata;
	public readonly byte[] Properties;
	public readonly PrepareFlags PrepareFlags;
	public readonly byte Version;

	// commit all writes except incomplete implicit transactions
	public bool CommitWrite => !(Type == RecType.Prepare &&
								 PrepareFlags.HasFlag(PrepareFlags.IsCommitted) &&
								 !PrepareFlags.HasFlag(PrepareFlags.TransactionEnd));

	public Rec(RecType type, int transaction, string streamId, string eventType, DateTime? timestamp, byte version,
		long? eventNumber = null,
		byte[] data = null,
		StreamMetadata metadata = null,
		byte[] properties = null,
		PrepareFlags prepareFlags = PrepareFlags.Data) {
		Ensure.NotNullOrEmpty(streamId, "streamId");
		Ensure.Nonnegative(transaction, "transaction");

		if (data != null && metadata != null)
			throw new Exception("two kinds of data were specified");

		Type = type;
		Id = Guid.NewGuid();
		Transaction = transaction;
		StreamId = streamId;
		EventType = eventType ?? string.Empty;
		TimeStamp = timestamp ?? DateTime.UtcNow;
		Version = version;
		EventNumber = eventNumber;
		Data = data;
		Metadata = metadata;
		Properties = properties;
		PrepareFlags = prepareFlags;
	}

	public static Rec Delete(int transaction, string stream, DateTime? timestamp = null,
		byte version = PrepareLogRecord.PrepareRecordVersion) {
		return new Rec(RecType.Delete, transaction, stream, SystemEventTypes.StreamDeleted, timestamp, version);
	}

	public static Rec CommittedDelete(int transaction, string stream, DateTime? timestamp = null,
		byte version = PrepareLogRecord.PrepareRecordVersion) {
		return new Rec(RecType.Delete, transaction, stream, SystemEventTypes.StreamDeleted, timestamp, version,
			prepareFlags: PrepareFlags.IsCommitted);
	}

	public static Rec TransSt(int transaction, string stream, DateTime? timestamp = null,
		byte version = PrepareLogRecord.PrepareRecordVersion) {
		return new Rec(RecType.TransStart, transaction, stream, null, timestamp, version);
	}

	public static Rec Prepare(int transaction, string stream, string eventType = null, DateTime? timestamp = null,
		long? eventNumber = null,
		byte[] data = null,
		StreamMetadata metadata = null,
		byte[] properties = null,
		PrepareFlags prepareFlags = PrepareFlags.Data,
		byte version = PrepareLogRecord.PrepareRecordVersion) {
		return new Rec(RecType.Prepare, transaction, stream, eventType, timestamp, version, eventNumber, data, metadata, properties, prepareFlags);
	}

	public static Rec Write(int transaction, string stream, string eventType = null, DateTime? timestamp = null,
		long? eventNumber = null,
		byte[] data = null,
		StreamMetadata metadata = null,
		byte[] properties = null,
		PrepareFlags prepareFlags = PrepareFlags.SingleWrite | PrepareFlags.IsCommitted,
		byte version = PrepareLogRecord.PrepareRecordVersion) {
		return new Rec(RecType.Prepare, transaction, stream, eventType, timestamp, version, eventNumber, data, metadata, properties, prepareFlags);
	}

	public static Rec TransEnd(int transaction, string stream, DateTime? timestamp = null,
		byte version = PrepareLogRecord.PrepareRecordVersion) {
		return new Rec(RecType.TransEnd, transaction, stream, null, timestamp, version);
	}

	public static Rec Commit(int transaction, string stream, DateTime? timestamp = null,
		byte version = CommitLogRecord.CommitRecordVersion) {
		return new Rec(RecType.Commit, transaction, stream, null, timestamp, version);
	}
}
