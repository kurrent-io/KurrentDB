// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Messages;
using EventStore.Core.Metrics;
using EventStore.Core.Services.Replication;
using EventStore.Core.Services.Storage;
using EventStore.Core.Services.Storage.EpochManager;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services;

public class ClusterStorageWriterService {
}

// see comment in StorageWriterService re CancellationToken handling
public class ClusterStorageWriterService<TStreamId> : StorageWriterService<TStreamId>,
	IAsyncHandle<ReplicationMessage.ReplicaSubscribed>,
	IAsyncHandle<ReplicationMessage.CreateChunk>,
	IAsyncHandle<ReplicationMessage.RawChunkBulk>,
	IAsyncHandle<ReplicationMessage.DataChunkBulk> {
	private static readonly ILogger Log = Serilog.Log.ForContext<ClusterStorageWriterService>();

	private readonly Func<long> _getLastIndexedPosition;
	private readonly TransactionFramer _framer;

	private Guid _subscriptionId;
	private TFChunk _activeChunk;
	private long _subscriptionPos;
	private long _ackedSubscriptionPos;

	public ClusterStorageWriterService(IPublisher bus,
		ISubscriber subscribeToBus,
		TimeSpan minFlushDelay,
		TFChunkDb db,
		TFChunkWriter writer,
		IIndexWriter<TStreamId> indexWriter,
		IRecordFactory<TStreamId> recordFactory,
		INameIndex<TStreamId> streamNameIndex,
		INameIndex<TStreamId> eventTypeIndex,
		TStreamId emptyEventTypeId,
		ISystemStreamLookup<TStreamId> systemStreams,
		IEpochManager epochManager,
		QueueStatsManager queueStatsManager,
		QueueTrackers trackers,
		IMaxTracker<long> flushSizeTracker,
		IDurationMaxTracker flushDurationTracker,
		Func<long> getLastIndexedPosition)
		: base(bus, subscribeToBus, minFlushDelay, db, writer, indexWriter, recordFactory, streamNameIndex,
			eventTypeIndex, emptyEventTypeId, systemStreams, epochManager, queueStatsManager, trackers,
			flushSizeTracker, flushDurationTracker) {
		Ensure.NotNull(getLastIndexedPosition, "getLastCommitPosition");

		_getLastIndexedPosition = getLastIndexedPosition;

		var lengthPrefixSuffixFramer = new LengthPrefixSuffixFramer(maxPackageSize: TFConsts.MaxLogRecordSize);
		var logRecordFramer = new LogRecordFramer(inner: lengthPrefixSuffixFramer);
		_framer = new TransactionFramer(inner: logRecordFramer);
		_framer.RegisterMessageArrivedCallback(OnTransactionUnframed);

		SubscribeToMessage<ReplicationMessage.ReplicaSubscribed>();
		SubscribeToMessage<ReplicationMessage.CreateChunk>();
		SubscribeToMessage<ReplicationMessage.RawChunkBulk>();
		SubscribeToMessage<ReplicationMessage.DataChunkBulk>();
	}

	public override async ValueTask HandleAsync(SystemMessage.StateChangeMessage message, CancellationToken token) {
		if (message.State is VNodeState.PreLeader) {
			_activeChunk?.MarkForDeletion();
			_activeChunk = null;

			_subscriptionId = Guid.Empty;
			_subscriptionPos = -1;
			_ackedSubscriptionPos = -1;
		}

		await base.HandleAsync(message, token);
	}

	async ValueTask IAsyncHandle<ReplicationMessage.ReplicaSubscribed>.HandleAsync(ReplicationMessage.ReplicaSubscribed message, CancellationToken token) {
		if (_activeChunk != null) {
			_activeChunk.MarkForDeletion();
			_activeChunk = null;
		}

		_framer.Reset();

		_subscriptionId = message.SubscriptionId;
		_ackedSubscriptionPos = _subscriptionPos = message.SubscriptionPosition;

		Log.Information(
			"=== SUBSCRIBED to [{leaderEndPoint},{leaderId:B}] at {subscriptionPosition} (0x{subscriptionPosition:X}). SubscriptionId: {subscriptionId:B}.",
			message.LeaderEndPoint, message.LeaderId, message.SubscriptionPosition, message.SubscriptionPosition,
			message.SubscriptionId);

		var writerCheck = Db.Config.WriterCheckpoint.ReadNonFlushed();
		if (message.SubscriptionPosition > writerCheck) {
			ReplicationFail(
				"Leader [{0},{1:B}] subscribed us at {2} (0x{3:X}), which is greater than our writer checkpoint {4} (0x{5:X}). REPLICATION BUG.",
				"Leader [{leaderEndpoint},{leaderId:B}] subscribed us at {subscriptionPosition} (0x{subscriptionPosition:X}), which is greater than our writer checkpoint {writerCheckpoint} (0x{writerCheckpoint:X}). REPLICATION BUG.",
				message.LeaderEndPoint, message.LeaderId, message.SubscriptionPosition,
				message.SubscriptionPosition, writerCheck, writerCheck);
		}

		if (message.SubscriptionPosition < writerCheck) {
			Log.Information(
				"Leader [{leaderEndPoint},{leaderId:B}] subscribed us at {subscriptionPosition} (0x{subscriptionPosition:X}), which is less than our writer checkpoint {writerCheckpoint} (0x{writerCheckpoint:X}). TRUNCATION IS NEEDED.",
				message.LeaderEndPoint, message.LeaderId, message.SubscriptionPosition,
				message.SubscriptionPosition, writerCheck, writerCheck);

			var lastIndexedPosition = _getLastIndexedPosition();
			if (message.SubscriptionPosition > lastIndexedPosition)
				Log.Information(
					"ONLINE TRUNCATION IS NEEDED. NOT IMPLEMENTED. OFFLINE TRUNCATION WILL BE PERFORMED. SHUTTING DOWN NODE.");
			else
				Log.Information(
					"OFFLINE TRUNCATION IS NEEDED (SubscribedAt {subscriptionPosition} (0x{subscriptionPosition:X}) <= LastCommitPosition {lastCommitPosition} (0x{lastCommitPosition:X})). SHUTTING DOWN NODE.",
					message.SubscriptionPosition, message.SubscriptionPosition, lastIndexedPosition,
					lastIndexedPosition);

			EpochRecord lastEpoch = EpochManager.GetLastEpoch();
			if (AreAnyCommittedRecordsTruncatedWithLastEpoch(message.SubscriptionPosition, lastEpoch,
				lastIndexedPosition)) {
				Log.Error(
					"Leader [{leaderEndPoint},{leaderId:B}] subscribed us at {subscriptionPosition} (0x{subscriptionPosition:X}), which is less than our last epoch and LastCommitPosition {lastCommitPosition} (0x{lastCommitPosition:X}) >= lastEpoch.EpochPosition {lastEpochPosition} (0x{lastEpochPosition:X}). That might be bad, especially if the LastCommitPosition is way beyond EpochPosition.",
					message.LeaderEndPoint, message.LeaderId, message.SubscriptionPosition,
					message.SubscriptionPosition, lastIndexedPosition, lastIndexedPosition, lastEpoch.EpochPosition,
					lastEpoch.EpochPosition);
				Log.Error(
					"ATTEMPT TO TRUNCATE EPOCH WITH COMMITTED RECORDS. THIS MAY BE BAD, BUT IT IS OK IF A NEWLY-ELECTED LEADER FAILS IMMEDIATELY AFTER ELECTION.");
			}

			Log.Information("Setting the truncate checkpoint to: {truncatePosition} (0x{truncatePosition:X})",
				message.SubscriptionPosition, message.SubscriptionPosition);
			Db.Config.TruncateCheckpoint.Write(message.SubscriptionPosition);
			Db.Config.TruncateCheckpoint.Flush();

			// try to write the new epoch position prior to shutting down to
			// avoid scanning the transaction log to find a valid epoch
			// when starting up for truncation
			var oldEpoch = EpochManager.GetLastEpoch();
			if (await EpochManager.TryTruncateBefore(message.SubscriptionPosition, token) is { } newEpoch) {
				if (newEpoch.EpochId != oldEpoch.EpochId) {
					Log.Information("Truncated epoch from "
					                + "E{oldEpochNumber}@{oldEpochPosition}:{oldEpochId:B} to "
					                + "E{newEpochNumber}@{newEpochPosition}:{newEpochId:B}",
						oldEpoch.EpochNumber, oldEpoch.EpochPosition, oldEpoch.EpochId,
						newEpoch.EpochNumber, newEpoch.EpochPosition, newEpoch.EpochId);
				} else {
					Log.Information("Truncation of epoch not required.");
				}
			} else {
				Log.Information("Could not find a valid epoch to truncate to before position: {truncatePosition} (0x{truncatePosition:X})",
					message.SubscriptionPosition, message.SubscriptionPosition);
				var epochs = await EpochManager.GetLastEpochs(int.MaxValue, token);
				if (epochs.Count > 0) {
					Log.Debug("Displaying cached epochs:");
					foreach (var epoch in epochs) {
						Log.Debug(
							"=== E{epochNumber}@{epochPosition}:{epochId:B}",
							epoch.EpochNumber, epoch.EpochPosition, epoch.EpochId);
					}
				} else {
					Log.Debug("No cached epochs were found");
				}
			}

			BlockWriter = true;
			Bus.Publish(new ClientMessage.RequestShutdown(exitProcess: true, shutdownHttp: true));
			return;
		}

		// subscription position == writer checkpoint
		// everything is ok
		Bus.Publish(new ReplicationMessage.AckLogPosition(
			subscriptionId: _subscriptionId,
			replicationLogPosition: _ackedSubscriptionPos,
			writerLogPosition: writerCheck));
	}

	private bool AreAnyCommittedRecordsTruncatedWithLastEpoch(long subscriptionPosition, EpochRecord lastEpoch,
		long lastCommitPosition) {
		return lastEpoch != null && subscriptionPosition <= lastEpoch.EpochPosition &&
		       lastCommitPosition >= lastEpoch.EpochPosition;
	}

	async ValueTask IAsyncHandle<ReplicationMessage.CreateChunk>.HandleAsync(ReplicationMessage.CreateChunk message, CancellationToken token) {
		if (_subscriptionId != message.SubscriptionId) return;

		if (_activeChunk != null) {
			_activeChunk.MarkForDeletion();
			_activeChunk = null;
		}

		_framer.Reset();

		if (!Db.TransformManager.SupportsTransform(message.ChunkHeader.TransformType)) {
			ReplicationFail(
				"Unsupported chunk transform: {0}",
				"Unsupported chunk transform: {transformType}.",
				message.ChunkHeader.TransformType);
		}

		if (message.IsScavengedChunk) {
			_activeChunk = await Db.Manager.CreateTempChunk(message.ChunkHeader, message.FileSize, token);
		} else {
			if (message.ChunkHeader.ChunkStartNumber == Db.Manager.ChunksCount) {
				await Writer.AddNewChunk(message.ChunkHeader, message.TransformHeader, message.FileSize, token);
			} else if (message.ChunkHeader.ChunkStartNumber + 1 == Db.Manager.ChunksCount) {
				// the requested chunk was already created. this is fine, it can happen if the follower created the
				// chunk in a previous run, was killed and re-subscribed to the leader at the beginning of the chunk.
				// i.e. this is the idempotent case
			} else {
				ReplicationFail(
					"Received request to create a new ongoing chunk #{0}-{1}, but current chunks count is {2}.",
					"Received request to create a new ongoing chunk #{chunkStartNumber}-{chunkEndNumber}, but current chunks count is {chunksCount}.",
					message.ChunkHeader.ChunkStartNumber, message.ChunkHeader.ChunkEndNumber,
					Db.Manager.ChunksCount);
			}
		}

		_subscriptionPos = message.ChunkHeader.ChunkStartPosition;
		_ackedSubscriptionPos = _subscriptionPos;
		Bus.Publish(new ReplicationMessage.AckLogPosition(
			subscriptionId: _subscriptionId,
			replicationLogPosition: _ackedSubscriptionPos,
			writerLogPosition: Writer.Position));
	}

	async ValueTask IAsyncHandle<ReplicationMessage.RawChunkBulk>.HandleAsync(ReplicationMessage.RawChunkBulk message, CancellationToken token) {
		if (_subscriptionId != message.SubscriptionId) return;
		if (_activeChunk is null)
			ReplicationFail(
				"Physical chunk bulk received, but we do not have active chunk.",
				"Physical chunk bulk received, but we do not have active chunk.");

		if (_activeChunk.ChunkHeader.ChunkStartNumber != message.ChunkStartNumber ||
		    _activeChunk.ChunkHeader.ChunkEndNumber != message.ChunkEndNumber) {
			Log.Error(
				"Received RawChunkBulk for TFChunk {chunkStartNumber}-{chunkEndNumber}, but active chunk is {activeChunk}.",
				message.ChunkStartNumber, message.ChunkEndNumber, _activeChunk);
			return;
		}

		if (_activeChunk.RawWriterPosition != message.RawPosition) {
			Log.Error(
				"Received RawChunkBulk at raw pos {rawPosition} (0x{rawPosition:X}) while current writer raw pos is {rawWriterPosition} (0x{rawWriterPosition:X}).",
				message.RawPosition, message.RawPosition, _activeChunk.RawWriterPosition,
				_activeChunk.RawWriterPosition);
			return;
		}

		if (!await _activeChunk.TryAppendRawData(message.RawBytes, token)) {
			ReplicationFail(
				"Could not append raw bytes to chunk {0}-{1}, raw pos: {2} (0x{3:X}), bytes length: {4} (0x{5:X}). Chunk file size: {6} (0x{7:X}).",
				"Could not append raw bytes to chunk {chunkStartNumber}-{chunkEndNumber}, raw pos: {rawPosition} (0x{rawPosition:X}), bytes length: {rawBytesLength} (0x{rawBytesLength:X}). Chunk file size: {chunkFileSize} (0x{chunkFileSize:X}).",
				message.ChunkStartNumber, message.ChunkEndNumber, message.RawPosition, message.RawPosition,
				message.RawBytes.Length, message.RawBytes.Length, _activeChunk.FileSize, _activeChunk.FileSize);
		}

		_subscriptionPos += message.RawBytes.Length;

		if (message.CompleteChunk) {
			Log.Debug("Completing raw chunk {chunkStartNumber}-{chunkEndNumber}...", message.ChunkStartNumber,
				message.ChunkEndNumber);
			await Writer.CompleteReplicatedRawChunk(_activeChunk, token);
			await Flush(token: token);

			_subscriptionPos = _activeChunk.ChunkHeader.ChunkEndPosition;
			_framer.Reset();
			_activeChunk = null;
		}

		if (message.CompleteChunk || _subscriptionPos > _ackedSubscriptionPos) {
			_ackedSubscriptionPos = _subscriptionPos;
			Bus.Publish(new ReplicationMessage.AckLogPosition(
				subscriptionId: _subscriptionId,
				replicationLogPosition: _ackedSubscriptionPos,
				writerLogPosition: Writer.Position));
		}
	}

	async ValueTask IAsyncHandle<ReplicationMessage.DataChunkBulk>.HandleAsync(ReplicationMessage.DataChunkBulk message, CancellationToken token) {
		Interlocked.Decrement(ref FlushMessagesInQueue);
		try {
			if (_subscriptionId != message.SubscriptionId) return;
			if (_activeChunk != null)
				ReplicationFail(
					"Data chunk bulk received, but we have active chunk for receiving raw chunk bulks.",
					"Data chunk bulk received, but we have active chunk for receiving raw chunk bulks.");

			if (Writer.NeedsNewChunk) {
				// for backwards compatibility with leaders running an old version (in case it doesn't send the CreateChunk message)
				await Writer.AddNewChunk(token: token);
			}

			var chunk = Writer.CurrentChunk;

			if (chunk.ChunkHeader.ChunkStartNumber != message.ChunkStartNumber ||
			    chunk.ChunkHeader.ChunkEndNumber != message.ChunkEndNumber) {
				Log.Error(
					"Received DataChunkBulk for TFChunk {chunkStartNumber}-{chunkEndNumber}, but active chunk is {activeChunkStartNumber}-{activeChunkEndNumber}.",
					message.ChunkStartNumber, message.ChunkEndNumber, chunk.ChunkHeader.ChunkStartNumber,
					chunk.ChunkHeader.ChunkEndNumber);
				return;
			}

			if (_subscriptionPos != message.SubscriptionPosition) {
				Log.Error(
					"Received DataChunkBulk at SubscriptionPosition {subscriptionPosition} (0x{subscriptionPosition:X}) while current SubscriptionPosition is {subscriptionPos} (0x{subscriptionPos:X}).",
					message.SubscriptionPosition, message.SubscriptionPosition, _subscriptionPos, _subscriptionPos);
				return;
			}

			await _framer.UnFrameData(new ArraySegment<byte>(message.DataBytes), token);
			_subscriptionPos += message.DataBytes.Length;

			if (message.CompleteChunk) {
				// for backwards compatibility with logs having incomplete transactions at the end of a chunk
				if (await _framer.UnFramePendingLogRecords(token) is { } numRecordsUnframed)
					Log.Warning("Incomplete transaction consisting of {numRecords} log records was found at the end of chunk: {chunkStartNumber}-{chunkEndNumber}.",
						numRecordsUnframed, message.ChunkStartNumber, message.ChunkEndNumber);

				Log.Debug("Completing data chunk {chunkStartNumber}-{chunkEndNumber}...", message.ChunkStartNumber,
					message.ChunkEndNumber);
				await Writer.CompleteChunk(token);

				if (_framer.HasData)
					ReplicationFail(
						"There is some data left in framer when completing chunk.",
						"There is some data left in framer when completing chunk.");

				_subscriptionPos = chunk.ChunkHeader.ChunkEndPosition;
				_framer.Reset();
			}
		} catch (Exception exc) {
			Log.Error(exc, "Exception in writer.");
			throw;
		} finally {
			await Flush(token: token);
		}

		if (message.CompleteChunk || _subscriptionPos > _ackedSubscriptionPos) {
			_ackedSubscriptionPos = _subscriptionPos;
			Bus.Publish(new ReplicationMessage.AckLogPosition(
				subscriptionId: _subscriptionId,
				replicationLogPosition: _ackedSubscriptionPos,
				// we leave it up to the Flush call above whether to truly flush or not
				writerLogPosition: Writer.Position));
		}
	}

	private async ValueTask OnTransactionUnframed(IEnumerable<ILogRecord> records, CancellationToken token) {
		// once we start the transaction, do not abort due to cancellation, we have no way to roll it back
		token.ThrowIfCancellationRequested();

		Writer.OpenTransaction();
		foreach (var record in records)
			if (await Writer.WriteToTransaction(record, CancellationToken.None) is null)
				ReplicationFail(
					"Failed to write replicated log record at position: {0}. Writer's position: {1}.",
					"Failed to write replicated log record at position: {recordPos}. Writer's position: {writerPos}.",
					record.LogPosition, Writer.Position);
		Writer.CommitTransaction();
	}

	[DoesNotReturn]
	private void ReplicationFail(string message, string messageStructured, params object[] args) {
		string msg;
		if (args is []) {
			Log.Fatal(messageStructured);
			msg = message;
		} else {
			Log.Fatal(messageStructured, args);
			msg = string.Format(message, args);
		}

		BlockWriter = true;
		Application.Exit(ExitCode.Error, msg);
		throw new Exception(msg);
	}
}
