// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
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
using EventRecord = KurrentDB.Core.Data.EventRecord;
using ILogger = Serilog.ILogger;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

// ReSharper disable StaticMemberInGenericType

namespace KurrentDB.Core.Services.Storage;

public abstract class StorageReaderWorker {
	protected static readonly ILogger Log = Serilog.Log.ForContext<StorageReaderWorker>();
}

public partial class StorageReaderWorker<TStreamId> :
	StorageReaderWorker,
	IAsyncHandle<StorageMessage.EffectiveStreamAclRequest>,
	IAsyncHandle<StorageMessage.StreamIdFromTransactionIdRequest>,
	IHandle<StorageMessage.BatchLogExpiredMessages> {

	private static IReadOnlyList<ResolvedEvent> EmptyRecords => [];
	private static readonly char[] LinkToSeparator = ['@'];

	private readonly IReadIndex<TStreamId> _readIndex;
	private readonly ISystemStreamLookup<TStreamId> _systemStreams;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly IPublisher _publisher;
	private readonly IVirtualStreamReader _virtualStreamReader;
	private readonly CancellationTokenMultiplexer _multiplexer;
	private readonly SecondaryIndexReaders _secondaryIndexReaders;
	private const int MaxPageSize = 4096;

	private readonly Message _scheduleBatchPeriodCompletion;
	private Atomic.Boolean _expiryPeriodRunning;
	private long _messagesExpiredInPeriod;

	public StorageReaderWorker(
		IPublisher publisher,
		IReadIndex<TStreamId> readIndex,
		ISystemStreamLookup<TStreamId> systemStreams,
		IReadOnlyCheckpoint writerCheckpoint,
		IVirtualStreamReader virtualStreamReader,
		SecondaryIndexReaders secondaryIndexReaders,
		long maxConcurrentReadersCount) {

		_publisher = publisher;
		_readIndex = Ensure.NotNull(readIndex);
		_systemStreams = Ensure.NotNull(systemStreams);
		_writerCheckpoint = Ensure.NotNull(writerCheckpoint);
		_virtualStreamReader = virtualStreamReader;
		_secondaryIndexReaders = secondaryIndexReaders;

		_multiplexer = new() { MaximumRetained = 100 };
		_scheduleBatchPeriodCompletion = TimerMessage.Schedule.Create(
			TimeSpan.FromSeconds(10),
			_publisher,
			new StorageMessage.BatchLogExpiredMessages());

		// Perf: HasConcurrencyLimit set to false means that the capacity of the internal IValueTaskSource pool is limited to avoid
		// inflation of the pool in the case of high (but rare) workloads, and let GC collect the sources that cannot be returned
		// to the pool
		_rateLimiter = maxConcurrentReadersCount > 0
			? new(maxConcurrentReadersCount) { ConcurrencyLevel = maxConcurrentReadersCount, HasConcurrencyLimit = false }
			: null;
	}

	async ValueTask IAsyncHandle<StorageMessage.EffectiveStreamAclRequest>.HandleAsync(StorageMessage.EffectiveStreamAclRequest msg, CancellationToken token) {
		Message reply;
		var cts = _multiplexer.Combine([token, msg.CancellationToken]);

		try {
			var acl = await _readIndex.GetEffectiveAcl(_readIndex.GetStreamId(msg.StreamId), cts.Token);
			reply = new StorageMessage.EffectiveStreamAclResponse(acl);
		} catch (OperationCanceledException ex) when (ex.CausedBy(cts, msg.CancellationToken)) {
			reply = new StorageMessage.OperationCancelledMessage(msg.CancellationToken);
		} catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token) {
			throw new OperationCanceledException(null, ex, cts.CancellationOrigin);
		} finally {
			await cts.DisposeAsync();
		}

		msg.Envelope.ReplyWith(reply);
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

			Log.Warning("Invalid link event payload [{LinkPayload}]: {EventRecord}", linkPayload, eventRecord);
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

	public void Handle(StorageMessage.BatchLogExpiredMessages message) {
		_expiryPeriodRunning.Value = false;
		var count = Interlocked.Exchange(ref _messagesExpiredInPeriod, 0);
		Log.Warning("StorageReader {0} read operations expired during the period", count);
	}

	// Counts expired messages and ensures the count is logged at the end of the period.
	// Returns whether the caller should additionally log a detailed message (limited to 5 per period).
	// Can run concurrently with the handling of BatchLogExpiredMessages.
	private bool LogExpiredMessage() {
		const int MaxDetailedExpiriesPerPeriod = 5;
		var count = Interlocked.Increment(ref _messagesExpiredInPeriod);

		if (count == MaxDetailedExpiriesPerPeriod + 1)
			Log.Warning("StorageReaderWorker: High rate of expired read messages detected. Stopping detailed expiry logs for remainder of period.");

		if (_expiryPeriodRunning.FalseToTrue())
			_publisher.Publish(_scheduleBatchPeriodCompletion);

		return count <= MaxDetailedExpiriesPerPeriod;
	}

	async ValueTask IAsyncHandle<StorageMessage.StreamIdFromTransactionIdRequest>.HandleAsync(StorageMessage.StreamIdFromTransactionIdRequest message, CancellationToken token) {
		var cts = _multiplexer.Combine([token, message.CancellationToken]);
		Message reply;
		try {
			var streamId = await _readIndex.GetEventStreamIdByTransactionId(message.TransactionId, cts.Token);
			var streamName = await _readIndex.GetStreamName(streamId, cts.Token);
			reply = new StorageMessage.StreamIdFromTransactionIdResponse(streamName);
		} catch (OperationCanceledException ex) when (ex.CausedBy(cts, message.CancellationToken)) {
			reply = new StorageMessage.OperationCancelledMessage(message.CancellationToken);
		} catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token) {
			throw new OperationCanceledException(null, ex, cts.CancellationOrigin);
		} finally {
			await cts.DisposeAsync();
		}

		message.Envelope.ReplyWith(reply);
	}
}
