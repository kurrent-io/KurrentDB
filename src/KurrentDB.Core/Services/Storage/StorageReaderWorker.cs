// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.TransactionLog.Checkpoint;
using ILogger = Serilog.ILogger;

// ReSharper disable StaticMemberInGenericType

namespace KurrentDB.Core.Services.Storage;

public abstract class StorageReaderWorker {
	protected static readonly ILogger Log = Serilog.Log.ForContext<StorageReaderWorker>();
}

public partial class StorageReaderWorker<TStreamId> :
	StorageReaderWorker,
	IAsyncHandle<ClientMessage.ReadEvent>,
	IAsyncHandle<ClientMessage.ReadStreamEventsBackward>,
	IAsyncHandle<ClientMessage.ReadStreamEventsForward>,
	IAsyncHandle<ClientMessage.ReadAllEventsForward>,
	IAsyncHandle<ClientMessage.ReadAllEventsBackward>,
	IAsyncHandle<ClientMessage.FilteredReadAllEventsForward>,
	IAsyncHandle<ClientMessage.FilteredReadAllEventsBackward>,
	IAsyncHandle<StorageMessage.EffectiveStreamAclRequest>,
	IAsyncHandle<StorageMessage.StreamIdFromTransactionIdRequest>,
	IHandle<StorageMessage.BatchLogExpiredMessages> {
	private static readonly ResolvedEvent[] EmptyRecords = [];
	private static readonly char[] LinkToSeparator = ['@'];

	private readonly IReadIndex<TStreamId> _readIndex;
	private readonly ISystemStreamLookup<TStreamId> _systemStreams;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly IPublisher _publisher;
	private readonly IVirtualStreamReader _virtualStreamReader;
	private readonly IBinaryInteger<int> _queueId;
	private const int MaxPageSize = 4096;
	private DateTime? _lastExpireTime;
	private long _expiredBatchCount;
	private bool _batchLoggingEnabled;

	public StorageReaderWorker(
		IPublisher publisher,
		IReadIndex<TStreamId> readIndex,
		ISystemStreamLookup<TStreamId> systemStreams,
		IReadOnlyCheckpoint writerCheckpoint,
		IVirtualStreamReader virtualStreamReader,
		int queueId) {
		_publisher = publisher;
		_readIndex = Ensure.NotNull(readIndex);
		_systemStreams = Ensure.NotNull(systemStreams);
		_writerCheckpoint = Ensure.NotNull(writerCheckpoint);
		_virtualStreamReader = virtualStreamReader;
		_queueId = queueId;
	}

	async ValueTask IAsyncHandle<StorageMessage.EffectiveStreamAclRequest>.HandleAsync(StorageMessage.EffectiveStreamAclRequest msg, CancellationToken token) {
		Message reply;
		var cts = token.LinkTo(msg.CancellationToken);

		try {
			var acl = await _readIndex.GetEffectiveAcl(_readIndex.GetStreamId(msg.StreamId), token);
			reply = new StorageMessage.EffectiveStreamAclResponse(acl);
		} catch (OperationCanceledException e) when (e.CausedBy(cts, msg.CancellationToken)) {
			reply = new StorageMessage.OperationCancelledMessage(msg.CancellationToken);
		} catch (OperationCanceledException ex) when (ex.CancellationToken == cts?.Token) {
			throw new OperationCanceledException(null, ex, cts.CancellationOrigin);
		} finally {
			cts?.Dispose();
		}

		msg.Envelope.ReplyWith(reply);
	}


	public void Handle(StorageMessage.BatchLogExpiredMessages message) {
		if (!_batchLoggingEnabled)
			return;
		if (_expiredBatchCount == 0) {
			_batchLoggingEnabled = false;
			Log.Warning("StorageReaderWorker #{0}: Batch logging disabled, read load is back to normal", _queueId);
			return;
		}

		Log.Warning("StorageReaderWorker #{0}: {1} read operations have expired", _queueId, _expiredBatchCount);
		_expiredBatchCount = 0;
		_publisher.Publish(TimerMessage.Schedule.Create(TimeSpan.FromSeconds(2), _publisher, new StorageMessage.BatchLogExpiredMessages(_queueId)));
	}

	private async ValueTask<IReadOnlyList<ResolvedEvent>> ResolveLinkToEvents(IReadOnlyList<EventRecord> records, bool resolveLinks, ClaimsPrincipal user, CancellationToken token) {
		var resolved = new ResolvedEvent[records.Count];
		if (resolveLinks) {
			for (var i = 0; i < records.Count; i++) {
				if (await ResolveLinkToEvent(records[i], null, token) is not { } rec)
					return null;

				resolved[i] = rec;
			}
		} else {
			for (int i = 0; i < records.Count; ++i) {
				resolved[i] = ResolvedEvent.ForUnresolvedEvent(records[i]);
			}
		}

		return resolved;
	}

	private async ValueTask<ResolvedEvent?> ResolveLinkToEvent(EventRecord eventRecord, long? commitPosition, CancellationToken token) {
		if (eventRecord.EventType is not SystemEventTypes.LinkTo) {
			return ResolvedEvent.ForUnresolvedEvent(eventRecord, commitPosition);
		}

		try {
			var linkPayload = Helper.UTF8NoBom.GetString(eventRecord.Data.Span);
			var parts = linkPayload.Split(LinkToSeparator, 2);
			if (long.TryParse(parts[0], out long eventNumber)) {
				var streamName = parts[1];
				var streamId = _readIndex.GetStreamId(streamName);
				var res = await _readIndex.ReadEvent(streamName, streamId, eventNumber, token);
				return res.Result is ReadEventResult.Success
					? ResolvedEvent.ForResolvedLink(res.Record, eventRecord, commitPosition)
					: ResolvedEvent.ForFailedResolvedLink(eventRecord, res.Result, commitPosition);
			}

			Log.Warning($"Invalid link event payload [{linkPayload}]: {eventRecord}");
			return ResolvedEvent.ForUnresolvedEvent(eventRecord, commitPosition);
		} catch (Exception exc) when (exc is not OperationCanceledException oce || oce.CancellationToken != token) {
			Log.Error(exc, "Error while resolving link for event record: {eventRecord}", eventRecord.ToString());
		}

		// return unresolved link
		return ResolvedEvent.ForFailedResolvedLink(eventRecord, ReadEventResult.Error, commitPosition);
	}

	private async ValueTask<IReadOnlyList<ResolvedEvent>> ResolveReadAllResult(List<CommitEventRecord> records, bool resolveLinks,
		ClaimsPrincipal user, CancellationToken token) {
		var result = new ResolvedEvent[records.Count];
		if (resolveLinks) {
			for (var i = 0; i < result.Length; ++i) {
				var record = records[i];
				if (await ResolveLinkToEvent(record.Event, record.CommitPosition, token) is not { } resolvedPair)
					return null;
				result[i] = resolvedPair;
			}
		} else {
			for (var i = 0; i < result.Length; ++i) {
				result[i] = ResolvedEvent.ForUnresolvedEvent(records[i].Event, records[i].CommitPosition);
			}
		}

		return result;
	}

	private bool LogExpiredMessage(DateTime expire) {
		if (!_lastExpireTime.HasValue) {
			_expiredBatchCount = 1;
			_lastExpireTime = expire;
			return true;
		}

		if (_batchLoggingEnabled) {
			_expiredBatchCount++;
			_lastExpireTime = expire;
			return false;
		}

		_expiredBatchCount++;
		if (_expiredBatchCount < 50)
			return true;

		if (expire - _lastExpireTime.Value > TimeSpan.FromSeconds(1)) {
			_expiredBatchCount = 1;
			_lastExpireTime = expire;

			return true;
		}

		//heuristic to match approximately >= 50 expired messages / second
		_batchLoggingEnabled = true;
		Log.Warning("StorageReaderWorker #{0}: Batch logging enabled, high rate of expired read messages detected", _queueId);
		_publisher.Publish(
			TimerMessage.Schedule.Create(TimeSpan.FromSeconds(2),
				_publisher,
				new StorageMessage.BatchLogExpiredMessages(_queueId))
		);
		_expiredBatchCount = 1;
		_lastExpireTime = expire;
		return false;
	}

	async ValueTask IAsyncHandle<StorageMessage.StreamIdFromTransactionIdRequest>.HandleAsync(StorageMessage.StreamIdFromTransactionIdRequest message, CancellationToken token) {
		var cts = token.LinkTo(message.CancellationToken);
		Message reply;
		try {
			var streamId = await _readIndex.GetEventStreamIdByTransactionId(message.TransactionId, token);
			var streamName = await _readIndex.GetStreamName(streamId, token);
			reply = new StorageMessage.StreamIdFromTransactionIdResponse(streamName);
		} catch (OperationCanceledException ex) when (ex.CausedBy(cts, message.CancellationToken)) {
			reply = new StorageMessage.OperationCancelledMessage(message.CancellationToken);
		} catch (OperationCanceledException ex) when (ex.CancellationToken == cts?.Token) {
			throw new OperationCanceledException(null, ex, cts.CancellationOrigin);
		} finally {
			cts?.Dispose();
		}

		message.Envelope.ReplyWith(reply);
	}
}
