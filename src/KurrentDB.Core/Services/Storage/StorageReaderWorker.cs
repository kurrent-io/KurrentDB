// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using DotNext.Threading;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Exceptions;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.Chunks.TFChunk;
using EventRecord = KurrentDB.Core.Data.EventRecord;
using ILogger = Serilog.ILogger;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

// ReSharper disable StaticMemberInGenericType

namespace KurrentDB.Core.Services.Storage;

public abstract class StorageReaderWorker {
	protected static readonly ILogger Log = Serilog.Log.ForContext<StorageReaderWorker>();
}

public partial class StorageReaderWorker<TStreamId> :
	StorageReaderWorker,
	IAsyncHandle<ClientMessage.ReadEvent>,
	IAsyncHandle<ClientMessage.ReadLogEvents>,
	IAsyncHandle<ClientMessage.ReadStreamEventsBackward>,
	IAsyncHandle<ClientMessage.ReadStreamEventsForward>,
	IAsyncHandle<ClientMessage.ReadAllEventsForward>,
	IAsyncHandle<ClientMessage.ReadAllEventsBackward>,
	IAsyncHandle<ClientMessage.FilteredReadAllEventsForward>,
	IAsyncHandle<ClientMessage.FilteredReadAllEventsBackward>,
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
		SecondaryIndexReaders secondaryIndexReaders) {

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
	}

	async ValueTask IAsyncHandle<ClientMessage.ReadEvent>.HandleAsync(ClientMessage.ReadEvent msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (LogExpiredMessage())
				Log.Debug(
					"Read Event operation has expired for Stream: {stream}, Event Number: {eventNumber}. Operation Expired at {expiryDateTime} after {lifetime:N0} ms.",
					msg.EventStreamId, msg.EventNumber, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		}

		var cts = _multiplexer.Combine([token, msg.CancellationToken]);
		ClientMessage.ReadEventCompleted ev;
		try {
			ev = await ReadEvent(msg, cts.Token);
		} catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token) {
			throw new OperationCanceledException(ex.Message, ex, cts.CancellationOrigin);
		} finally {
			await cts.DisposeAsync();
		}

		msg.Envelope.ReplyWith(ev);
	}

	async ValueTask IAsyncHandle<ClientMessage.ReadStreamEventsForward>.HandleAsync(ClientMessage.ReadStreamEventsForward msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new ClientMessage.ReadStreamEventsForwardCompleted(
					msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, ReadStreamResult.Expired,
					ResolvedEvent.EmptyArray, default, default, default, -1, default, true, default));
			}

			if (LogExpiredMessage())
				Log.Debug(
					"Read Stream Events Forward operation has expired for Stream: {stream}, From Event Number: {fromEventNumber}, Max Count: {maxCount}. Operation Expired at {expiryDateTime} after {lifetime:N0} ms.",
					msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		}

		ClientMessage.ReadStreamEventsForwardCompleted res;
		var cts = _multiplexer.Combine([token, msg.CancellationToken]);
		try {
			res = await (SystemStreams.IsInMemoryStream(msg.EventStreamId)
				? _virtualStreamReader.ReadForwards(msg, cts.Token)
				: ReadStreamEventsForward(msg, cts.Token));
		} catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token) {
			throw new OperationCanceledException(null, ex, cts.CancellationOrigin);
		} finally {
			await cts.DisposeAsync();
		}

		switch (res.Result) {
			case ReadStreamResult.Success
				or ReadStreamResult.NoStream
				or ReadStreamResult.NotModified
				when msg.LongPollTimeout is { } longPollTimeout && res.FromEventNumber > res.LastEventNumber:
				_publisher.Publish(new SubscriptionMessage.PollStream(
					msg.EventStreamId, res.TfLastCommitPosition, res.LastEventNumber,
					DateTime.UtcNow + longPollTimeout, msg));

				break;
			case ReadStreamResult.StreamDeleted
				or ReadStreamResult.Error
				or ReadStreamResult.AccessDenied
				or ReadStreamResult.Success
				or ReadStreamResult.NoStream
				or ReadStreamResult.NotModified:
				msg.Envelope.ReplyWith(res);
				break;
			default:
				throw new ArgumentOutOfRangeException($"Unknown ReadStreamResult: {res.Result}");
		}
	}

	async ValueTask IAsyncHandle<ClientMessage.ReadStreamEventsBackward>.HandleAsync(
		ClientMessage.ReadStreamEventsBackward msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new ClientMessage.ReadStreamEventsBackwardCompleted(
					msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, ReadStreamResult.Expired,
					ResolvedEvent.EmptyArray, default, default, default, -1, default, true, default));
			}

			if (LogExpiredMessage())
				Log.Debug(
					"Read Stream Events Backward operation has expired for Stream: {stream}, From Event Number: {fromEventNumber}, Max Count: {maxCount}. Operation Expired at {expiryDateTime} after {lifetime:N0} ms.",
					msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		}

		var cts = _multiplexer.Combine([token, msg.CancellationToken]);
		ClientMessage.ReadStreamEventsBackwardCompleted res;
		try {
			res = await (SystemStreams.IsInMemoryStream(msg.EventStreamId)
				? _virtualStreamReader.ReadBackwards(msg, cts.Token)
				: ReadStreamEventsBackward(msg, cts.Token));
		} catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token) {
			throw new OperationCanceledException(null, ex, cts.CancellationOrigin);
		} finally {
			await cts.DisposeAsync();
		}

		msg.Envelope.ReplyWith(res);
	}

	async ValueTask IAsyncHandle<ClientMessage.ReadAllEventsForward>.HandleAsync(ClientMessage.ReadAllEventsForward msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new ClientMessage.ReadAllEventsForwardCompleted(
					msg.CorrelationId, ReadAllResult.Expired,
					default, ResolvedEvent.EmptyArray, default, default, default,
					currentPos: new TFPos(msg.CommitPosition, msg.PreparePosition),
					TFPos.Invalid, TFPos.Invalid, default));
			}

			if (LogExpiredMessage())
				Log.Debug(
					"Read All Stream Events Forward operation has expired for C:{commitPosition}/P:{preparePosition}. Operation Expired at {expiryDateTime} after {lifetime:N0} ms.",
					msg.CommitPosition, msg.PreparePosition, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		}

		var res = await ReadAllEventsForward(msg, token);
		switch (res.Result) {
			case ReadAllResult.Success when msg.LongPollTimeout is { } longPoolTimeout && res is { IsEndOfStream: true, Events: [] }:
			case ReadAllResult.NotModified when msg.LongPollTimeout.TryGetValue(out longPoolTimeout)
			                                    && res.IsEndOfStream
			                                    && res.CurrentPos.CommitPosition > res.TfLastCommitPosition:
				_publisher.Publish(new SubscriptionMessage.PollStream(
					SubscriptionsService.AllStreamsSubscriptionId, res.TfLastCommitPosition, null,
					DateTime.UtcNow + longPoolTimeout, msg));

				break;
			case ReadAllResult.Error
				or ReadAllResult.AccessDenied
				or ReadAllResult.InvalidPosition
				or ReadAllResult.Success
				or ReadAllResult.NotModified:
				msg.Envelope.ReplyWith(res);
				break;
			default:
				throw new ArgumentOutOfRangeException($"Unknown ReadAllResult: {res.Result}");
		}
	}

	async ValueTask IAsyncHandle<ClientMessage.ReadAllEventsBackward>.HandleAsync(ClientMessage.ReadAllEventsBackward msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new ClientMessage.ReadAllEventsBackwardCompleted(
					msg.CorrelationId, ReadAllResult.Expired,
					default, ResolvedEvent.EmptyArray, default, default, default,
					currentPos: new TFPos(msg.CommitPosition, msg.PreparePosition),
					TFPos.Invalid, TFPos.Invalid, default));
			}

			if (LogExpiredMessage())
				Log.Debug(
					"Read All Stream Events Backward operation has expired for C:{commitPosition}/P:{preparePosition}. Operation Expired at {expiryDateTime} after {lifetime:N0} ms.",
					msg.CommitPosition, msg.PreparePosition, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		}

		msg.Envelope.ReplyWith(await ReadAllEventsBackward(msg, token));
	}

	async ValueTask IAsyncHandle<ClientMessage.FilteredReadAllEventsForward>.HandleAsync(ClientMessage.FilteredReadAllEventsForward msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new ClientMessage.FilteredReadAllEventsForwardCompleted(
					msg.CorrelationId, FilteredReadAllResult.Expired,
					default, ResolvedEvent.EmptyArray, default, default, default,
					currentPos: new TFPos(msg.CommitPosition, msg.PreparePosition),
					TFPos.Invalid, TFPos.Invalid, default, default, default));
			}

			Log.Debug(
				"Read All Stream Events Forward Filtered operation has expired for C:{0}/P:{1}. Operation Expired at {2} after {lifetime:N0} ms.",
				msg.CommitPosition, msg.PreparePosition, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		}

		var res = await FilteredReadAllEventsForward(msg, token);
		switch (res.Result) {
			case FilteredReadAllResult.Success
				when msg.LongPollTimeout is { } longPollTimeout && res is { IsEndOfStream: true, Events: [] }:
			case FilteredReadAllResult.NotModified
				when msg.LongPollTimeout.TryGetValue(out longPollTimeout)
				     && res.IsEndOfStream
				     && res.CurrentPos.CommitPosition > res.TfLastCommitPosition:
				_publisher.Publish(new SubscriptionMessage.PollStream(
					SubscriptionsService.AllStreamsSubscriptionId, res.TfLastCommitPosition, null,
					DateTime.UtcNow + longPollTimeout, msg));

				break;
			case FilteredReadAllResult.Error
				or FilteredReadAllResult.AccessDenied
				or FilteredReadAllResult.InvalidPosition
				or FilteredReadAllResult.Success
				or FilteredReadAllResult.NotModified:
				msg.Envelope.ReplyWith(res);
				break;
			default:
				throw new ArgumentOutOfRangeException($"Unknown ReadAllResult: {res.Result}");
		}
	}

	async ValueTask IAsyncHandle<ClientMessage.FilteredReadAllEventsBackward>.HandleAsync(ClientMessage.FilteredReadAllEventsBackward msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new ClientMessage.FilteredReadAllEventsBackwardCompleted(
					msg.CorrelationId, FilteredReadAllResult.Expired,
					default, ResolvedEvent.EmptyArray, default, default, default,
					currentPos: new TFPos(msg.CommitPosition, msg.PreparePosition),
					TFPos.Invalid, TFPos.Invalid, default, default));
			}

			Log.Debug(
				"Read All Stream Events Backward Filtered operation has expired for C:{0}/P:{1}. Operation Expired at {2} after {lifetime:N0} ms.",
				msg.CommitPosition, msg.PreparePosition, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		}

		var res = await FilteredReadAllEventsBackward(msg, token);
		switch (res.Result) {
			case FilteredReadAllResult.Success
				when msg.LongPollTimeout is { } longPollTimeout && res is { IsEndOfStream: true, Events: [] }:
			case FilteredReadAllResult.NotModified
				when msg.LongPollTimeout.TryGetValue(out longPollTimeout)
				     && res.IsEndOfStream
				     && res.CurrentPos.CommitPosition > res.TfLastCommitPosition:
				_publisher.Publish(new SubscriptionMessage.PollStream(
					SubscriptionsService.AllStreamsSubscriptionId, res.TfLastCommitPosition, null,
					DateTime.UtcNow + longPollTimeout, msg));

				break;
			case FilteredReadAllResult.Error
				or FilteredReadAllResult.AccessDenied
				or FilteredReadAllResult.InvalidPosition
				or FilteredReadAllResult.Success
				or FilteredReadAllResult.NotModified:
				msg.Envelope.ReplyWith(res);
				break;
			default:
				throw new ArgumentOutOfRangeException($"Unknown ReadAllResult: {res.Result}");
		}
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

	private async ValueTask<ClientMessage.ReadEventCompleted> ReadEvent(ClientMessage.ReadEvent msg, CancellationToken token) {
		try {
			var streamName = msg.EventStreamId;
			var streamId = _readIndex.GetStreamId(streamName);
			var result = await _readIndex.ReadEvent(streamName, streamId, msg.EventNumber, token);

			ResolvedEvent record;
			switch (result) {
				case { Result: ReadEventResult.Success } when msg.ResolveLinkTos:
					if ((await ResolveLinkToEvent(result.Record, null, token)).TryGetValue(out record))
						break;

					return NoData(ReadEventResult.AccessDenied);
				case { Result: ReadEventResult.NoStream or ReadEventResult.NotFound, OriginalStreamExists: true }
					when _systemStreams.IsMetaStream(streamId):
					return NoData(ReadEventResult.Success);
				default:
					record = ResolvedEvent.ForUnresolvedEvent(result.Record);
					break;
			}

			return new(msg.CorrelationId, msg.EventStreamId, result.Result, record, result.Metadata, false, null);
		} catch (Exception exc) when (exc is not OperationCanceledException oce || oce.CancellationToken != token) {
			Log.Error(exc, "Error during processing ReadEvent request.");
			return NoData(ReadEventResult.Error, exc.Message);
		}

		ClientMessage.ReadEventCompleted NoData(ReadEventResult result, string error = null)
			=> new(msg.CorrelationId, msg.EventStreamId, result, ResolvedEvent.EmptyEvent, null, false, error);
	}

	private async ValueTask<ClientMessage.ReadStreamEventsForwardCompleted> ReadStreamEventsForward(ClientMessage.ReadStreamEventsForward msg, CancellationToken token) {
		var lastIndexPosition = _readIndex.LastIndexedPosition;
		try {
			if (msg.MaxCount > MaxPageSize) {
				throw new ArgumentException($"Read size too big, should be less than {MaxPageSize} items");
			}

			var streamName = msg.EventStreamId;
			var streamId = _readIndex.GetStreamId(msg.EventStreamId);
			if (msg.ValidationStreamVersion is { } streamVer &&
			    await _readIndex.GetStreamLastEventNumber(streamId, token) == streamVer)
				return NoData(ReadStreamResult.NotModified, lastIndexPosition, streamVer);

			var result = await _readIndex.ReadStreamEventsForward(streamName, streamId, msg.FromEventNumber, msg.MaxCount, token);
			CheckEventsOrder(msg, result);
			if (await ResolveLinkToEvents(result.Records, msg.ResolveLinkTos, msg.User, token) is not { } resolvedPairs)
				return NoData(ReadStreamResult.AccessDenied, lastIndexPosition);

			return new(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount,
				(ReadStreamResult)result.Result, resolvedPairs, result.Metadata, false, string.Empty,
				result.NextEventNumber, result.LastEventNumber, result.IsEndOfStream, lastIndexPosition);
		} catch (Exception exc) when (exc is not OperationCanceledException oce || oce.CancellationToken != token) {
			Log.Error(exc, "Error during processing ReadStreamEventsForward request.");
			return NoData(ReadStreamResult.Error, lastIndexPosition, error: exc.Message);
		}

		static void CheckEventsOrder(ClientMessage.ReadStreamEventsForward msg, IndexReadStreamResult result) {
			for (var index = 1; index < result.Records.Length; index++) {
				if (result.Records[index].EventNumber != result.Records[index - 1].EventNumber + 1) {
					throw new Exception(
						$"Invalid order of events has been detected in read index for the event stream '{msg.EventStreamId}'. " +
						$"The event {result.Records[index].EventNumber} at position {result.Records[index].LogPosition} goes after the event {result.Records[index - 1].EventNumber} at position {result.Records[index - 1].LogPosition}");
				}
			}
		}

		ClientMessage.ReadStreamEventsForwardCompleted NoData(ReadStreamResult result, long lastIndexedPosition, long lastEventNumber = -1, long nextEventNumber = -1, string error = null)
			=> new(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, result,
				EmptyRecords, null, false, error ?? string.Empty, nextEventNumber, lastEventNumber, true, lastIndexedPosition);
	}

	private async ValueTask<ClientMessage.ReadStreamEventsBackwardCompleted> ReadStreamEventsBackward(ClientMessage.ReadStreamEventsBackward msg, CancellationToken token) {
		var lastIndexedPosition = _readIndex.LastIndexedPosition;
		try {
			if (msg.MaxCount > MaxPageSize) {
				throw new ArgumentException($"Read size too big, should be less than {MaxPageSize} items");
			}

			var streamName = msg.EventStreamId;
			var streamId = _readIndex.GetStreamId(msg.EventStreamId);
			if (msg.ValidationStreamVersion is { } streamVer &&
			    await _readIndex.GetStreamLastEventNumber(streamId, token) == streamVer)
				return NoData(ReadStreamResult.NotModified, streamVer);

			var result = await _readIndex.ReadStreamEventsBackward(streamName, streamId, msg.FromEventNumber, msg.MaxCount, token);
			CheckEventsOrder(msg, result);
			if (await ResolveLinkToEvents(result.Records, msg.ResolveLinkTos, msg.User, token) is not { } resolvedPairs)
				return NoData(ReadStreamResult.AccessDenied);

			return new(msg.CorrelationId, msg.EventStreamId, result.FromEventNumber, result.MaxCount,
				(ReadStreamResult)result.Result, resolvedPairs, result.Metadata, false, string.Empty,
				result.NextEventNumber, result.LastEventNumber, result.IsEndOfStream, lastIndexedPosition);
		} catch (Exception exc) when (exc is not OperationCanceledException oce || oce.CancellationToken != token) {
			Log.Error(exc, "Error during processing ReadStreamEventsBackward request.");
			return NoData(ReadStreamResult.Error, error: exc.Message);
		}

		static void CheckEventsOrder(ClientMessage.ReadStreamEventsBackward msg, IndexReadStreamResult result) {
			for (var index = 1; index < result.Records.Length; index++) {
				if (result.Records[index].EventNumber != result.Records[index - 1].EventNumber - 1) {
					throw new Exception(
						$"Invalid order of events has been detected in read index for the event stream '{msg.EventStreamId}'. " +
						$"The event {result.Records[index].EventNumber} at position {result.Records[index].LogPosition} goes after the event {result.Records[index - 1].EventNumber} at position {result.Records[index - 1].LogPosition}");
				}
			}
		}

		ClientMessage.ReadStreamEventsBackwardCompleted NoData(ReadStreamResult result, long lastEventNumber = -1, long nextEventNumber = -1, string error = null)
			=> new(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, result,
				EmptyRecords, null, false, error ?? string.Empty, nextEventNumber, lastEventNumber, true, lastIndexedPosition);
	}

	private async ValueTask<ClientMessage.ReadAllEventsForwardCompleted> ReadAllEventsForward(ClientMessage.ReadAllEventsForward msg, CancellationToken token) {
		var pos = new TFPos(msg.CommitPosition, msg.PreparePosition);
		var lastIndexedPosition = _readIndex.LastIndexedPosition;
		try {
			if (msg.MaxCount > MaxPageSize) {
				throw new ArgumentException($"Read size too big, should be less than {MaxPageSize} items");
			}

			if (pos == TFPos.HeadOfTf) {
				var checkpoint = _writerCheckpoint.Read();
				pos = new TFPos(checkpoint, checkpoint);
			}

			if (pos.CommitPosition < 0 || pos.PreparePosition < 0)
				return NoData(ReadAllResult.InvalidPosition, "Invalid position.");
			if (msg.ValidationTfLastCommitPosition == lastIndexedPosition)
				return NoData(ReadAllResult.NotModified);

			var res = await _readIndex.ReadAllEventsForward(pos, msg.MaxCount, token);
			if (await ResolveReadAllResult(res.Records, msg.ResolveLinkTos, msg.User, token) is not { } resolved)
				return NoData(ReadAllResult.AccessDenied);

			var metadata = await _readIndex.GetStreamMetadata(_systemStreams.AllStream, token);
			return new(msg.CorrelationId, ReadAllResult.Success, null, resolved, metadata,
				false, msg.MaxCount, res.CurrentPos, res.NextPos, res.PrevPos, lastIndexedPosition);
		} catch (Exception exc) when (exc is InvalidReadException or UnableToReadPastEndOfStreamException) {
			Log.Warning(exc, "Error during processing ReadAllEventsBackward request. The read appears to be at an invalid position.");
			return NoData(ReadAllResult.InvalidPosition, exc.Message);
		} catch (Exception exc) when (exc is not OperationCanceledException oce || oce.CancellationToken != token) {
			Log.Error(exc, "Error during processing ReadAllEventsForward request.");
			return NoData(ReadAllResult.Error, exc.Message);
		}

		ClientMessage.ReadAllEventsForwardCompleted NoData(ReadAllResult result, string error = null)
			=> new(msg.CorrelationId, result, error, ResolvedEvent.EmptyArray, null, false,
				msg.MaxCount, pos, TFPos.Invalid, TFPos.Invalid, lastIndexedPosition);
	}

	private async ValueTask<ClientMessage.ReadAllEventsBackwardCompleted> ReadAllEventsBackward(ClientMessage.ReadAllEventsBackward msg, CancellationToken token) {
		var pos = new TFPos(msg.CommitPosition, msg.PreparePosition);
		var lastIndexedPosition = _readIndex.LastIndexedPosition;
		try {
			if (msg.MaxCount > MaxPageSize) {
				throw new ArgumentException($"Read size too big, should be less than {MaxPageSize} items");
			}

			if (pos == TFPos.HeadOfTf) {
				var checkpoint = _writerCheckpoint.Read();
				pos = new TFPos(checkpoint, checkpoint);
			}

			if (pos.CommitPosition < 0 || pos.PreparePosition < 0)
				return NoData(ReadAllResult.InvalidPosition, "Invalid position.");
			if (msg.ValidationTfLastCommitPosition == lastIndexedPosition)
				return NoData(ReadAllResult.NotModified);

			var res = await _readIndex.ReadAllEventsBackward(pos, msg.MaxCount, token);
			if (await ResolveReadAllResult(res.Records, msg.ResolveLinkTos, msg.User, token) is not { } resolved)
				return NoData(ReadAllResult.AccessDenied);

			var metadata = await _readIndex.GetStreamMetadata(_systemStreams.AllStream, token);
			return new(msg.CorrelationId, ReadAllResult.Success, null, resolved, metadata,
				false, msg.MaxCount, res.CurrentPos, res.NextPos, res.PrevPos, lastIndexedPosition);
		} catch (Exception exc) when (exc is InvalidReadException or UnableToReadPastEndOfStreamException) {
			Log.Warning(exc, "Error during processing ReadAllEventsBackward request. The read appears to be at an invalid position.");
			return NoData(ReadAllResult.InvalidPosition, exc.Message);
		} catch (Exception exc) when (exc is not OperationCanceledException oce || oce.CancellationToken != token) {
			Log.Error(exc, "Error during processing ReadAllEventsBackward request.");
			return NoData(ReadAllResult.Error, exc.Message);
		}

		ClientMessage.ReadAllEventsBackwardCompleted NoData(ReadAllResult result, string error = null)
			=> new(msg.CorrelationId, result, error, ResolvedEvent.EmptyArray, null, false,
				msg.MaxCount, pos, TFPos.Invalid, TFPos.Invalid, lastIndexedPosition);
	}

	private async ValueTask<ClientMessage.FilteredReadAllEventsForwardCompleted> FilteredReadAllEventsForward(ClientMessage.FilteredReadAllEventsForward msg, CancellationToken token) {
		var pos = new TFPos(msg.CommitPosition, msg.PreparePosition);
		var lastIndexedPosition = _readIndex.LastIndexedPosition;
		try {
			if (msg.MaxCount > MaxPageSize) {
				throw new ArgumentException($"Read size too big, should be less than {MaxPageSize} items");
			}

			if (pos == TFPos.HeadOfTf) {
				var checkpoint = _writerCheckpoint.Read();
				pos = new TFPos(checkpoint, checkpoint);
			}

			if (pos.CommitPosition < 0 || pos.PreparePosition < 0)
				return NoData(FilteredReadAllResult.InvalidPosition, "Invalid position.");
			if (msg.ValidationTfLastCommitPosition == lastIndexedPosition)
				return NoData(FilteredReadAllResult.NotModified);

			var res = await _readIndex.ReadAllEventsForwardFiltered(pos, msg.MaxCount, msg.MaxSearchWindow, msg.EventFilter, token);
			if (await ResolveReadAllResult(res.Records, msg.ResolveLinkTos, msg.User, token) is not { } resolved)
				return NoData(FilteredReadAllResult.AccessDenied);

			var metadata = await _readIndex.GetStreamMetadata(_systemStreams.AllStream, token);
			return new ClientMessage.FilteredReadAllEventsForwardCompleted(
				msg.CorrelationId, FilteredReadAllResult.Success, null, resolved, metadata, false,
				msg.MaxCount, res.CurrentPos, res.NextPos, res.PrevPos, lastIndexedPosition, res.IsEndOfStream,
				res.ConsideredEventsCount);
		} catch (Exception exc) when (exc is InvalidReadException or UnableToReadPastEndOfStreamException) {
			Log.Warning(exc, "Error during processing ReadAllEventsForwardFiltered request. The read appears to be at an invalid position.");
			return NoData(FilteredReadAllResult.InvalidPosition, exc.Message);
		} catch (Exception exc) when (exc is not OperationCanceledException oce || oce.CancellationToken != token) {
			Log.Error(exc, "Error during processing ReadAllEventsForwardFiltered request.");
			return NoData(FilteredReadAllResult.Error, exc.Message);
		}

		ClientMessage.FilteredReadAllEventsForwardCompleted NoData(FilteredReadAllResult result, string error = null)
			=> new(msg.CorrelationId, result, error, ResolvedEvent.EmptyArray, null, false,
				msg.MaxCount, pos, TFPos.Invalid, TFPos.Invalid, lastIndexedPosition, false, 0L);
	}

	private async ValueTask<ClientMessage.FilteredReadAllEventsBackwardCompleted> FilteredReadAllEventsBackward(ClientMessage.FilteredReadAllEventsBackward msg, CancellationToken token) {
		var pos = new TFPos(msg.CommitPosition, msg.PreparePosition);
		var lastIndexedPosition = _readIndex.LastIndexedPosition;
		try {
			if (msg.MaxCount > MaxPageSize) {
				throw new ArgumentException($"Read size too big, should be less than {MaxPageSize} items");
			}

			if (pos == TFPos.HeadOfTf) {
				var checkpoint = _writerCheckpoint.Read();
				pos = new TFPos(checkpoint, checkpoint);
			}

			if (pos.CommitPosition < 0 || pos.PreparePosition < 0)
				return NoData(FilteredReadAllResult.InvalidPosition, "Invalid position.");
			if (msg.ValidationTfLastCommitPosition == lastIndexedPosition)
				return NoData(FilteredReadAllResult.NotModified);

			var res = await _readIndex.ReadAllEventsBackwardFiltered(pos, msg.MaxCount, msg.MaxSearchWindow,
				msg.EventFilter, token);
			if (await ResolveReadAllResult(res.Records, msg.ResolveLinkTos, msg.User, token) is not { } resolved)
				return NoData(FilteredReadAllResult.AccessDenied);

			var metadata = await _readIndex.GetStreamMetadata(_systemStreams.AllStream, token);
			return new ClientMessage.FilteredReadAllEventsBackwardCompleted(
				msg.CorrelationId, FilteredReadAllResult.Success, null, resolved, metadata, false,
				msg.MaxCount,
				res.CurrentPos, res.NextPos, res.PrevPos, lastIndexedPosition, res.IsEndOfStream);
		} catch (Exception exc) when (exc is InvalidReadException or UnableToReadPastEndOfStreamException) {
			Log.Warning(exc, "Error during processing ReadAllEventsBackwardFiltered request. The read appears to be at an invalid position.");
			return NoData(FilteredReadAllResult.InvalidPosition, exc.Message);
		} catch (Exception exc) when (exc is not OperationCanceledException oce || oce.CancellationToken != token) {
			Log.Error(exc, "Error during processing ReadAllEventsBackwardFiltered request.");
			return NoData(FilteredReadAllResult.Error, exc.Message);
		}

		ClientMessage.FilteredReadAllEventsBackwardCompleted NoData(FilteredReadAllResult result, string error = null)
			=> new(msg.CorrelationId, result, error, ResolvedEvent.EmptyArray, null, false,
				msg.MaxCount, pos, TFPos.Invalid, TFPos.Invalid, lastIndexedPosition, false);
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
