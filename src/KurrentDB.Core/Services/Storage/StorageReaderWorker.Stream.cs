// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using static KurrentDB.Core.Messages.ClientMessage;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;

namespace KurrentDB.Core.Services.Storage;

public partial class StorageReaderWorker<TStreamId> {
	async ValueTask IAsyncHandle<ReadStreamEventsForward>.HandleAsync(ReadStreamEventsForward msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new ReadStreamEventsForwardCompleted(
					msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, ReadStreamResult.Expired,
					ResolvedEvent.EmptyArray, default, default, default, -1, default, true, default));
			}

			if (LogExpiredMessage(msg.Expires))
				Log.Debug(
					"Read Stream Events Forward operation has expired for Stream: {stream}, From Event Number: {fromEventNumber}, Max Count: {maxCount}. Operation Expired at {expiryDateTime} after {lifetime:N0} ms.",
					msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		}

		ReadStreamEventsForwardCompleted res;
		var cts = token.LinkTo(msg.CancellationToken);
		try {
			res = SystemStreams.IsInMemoryStream(msg.EventStreamId)
				? await _virtualStreamReader.ReadForwards(msg, token)
				: await ReadStreamEventsForward(msg, token);
		} catch (OperationCanceledException ex) when (ex.CancellationToken == cts?.Token) {
			throw new OperationCanceledException(null, ex, cts.CancellationOrigin);
		} finally {
			cts?.Dispose();
		}

		switch (res.Result) {
			case ReadStreamResult.Success:
			case ReadStreamResult.NoStream:
			case ReadStreamResult.NotModified:
				if (msg.LongPollTimeout.HasValue && res.FromEventNumber > res.LastEventNumber) {
					_publisher.Publish(new SubscriptionMessage.PollStream(
						msg.EventStreamId, res.TfLastCommitPosition, res.LastEventNumber,
						DateTime.UtcNow + msg.LongPollTimeout.Value, msg));
				} else {
					msg.Envelope.ReplyWith(res);
				}

				break;
			case ReadStreamResult.StreamDeleted:
			case ReadStreamResult.Error:
			case ReadStreamResult.AccessDenied:
				msg.Envelope.ReplyWith(res);
				break;
			default:
				throw new ArgumentOutOfRangeException($"Unknown ReadStreamResult: {res.Result}");
		}
	}

	async ValueTask IAsyncHandle<ReadStreamEventsBackward>.HandleAsync(ReadStreamEventsBackward msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new ReadStreamEventsBackwardCompleted(
					msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, ReadStreamResult.Expired,
					ResolvedEvent.EmptyArray, default, default, default, -1, default, true, default));
			}

			if (LogExpiredMessage(msg.Expires))
				Log.Debug(
					"Read Stream Events Backward operation has expired for Stream: {stream}, From Event Number: {fromEventNumber}, Max Count: {maxCount}. Operation Expired at {expiryDateTime} after {lifetime:N0} ms.",
					msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		}

		var cts = token.LinkTo(msg.CancellationToken);
		try {
			var res = SystemStreams.IsInMemoryStream(msg.EventStreamId)
				? await _virtualStreamReader.ReadBackwards(msg, token)
				: await ReadStreamEventsBackward(msg, token);

			msg.Envelope.ReplyWith(res);
		} catch (OperationCanceledException ex) when (ex.CancellationToken == cts?.Token) {
			throw new OperationCanceledException(null, ex, cts.CancellationOrigin);
		} finally {
			cts?.Dispose();
		}
	}

	private async ValueTask<ReadStreamEventsForwardCompleted> ReadStreamEventsForward(ReadStreamEventsForward msg, CancellationToken token) {
		var lastIndexPosition = _readIndex.LastIndexedPosition;
		try {
			if (msg.MaxCount > MaxPageSize) {
				throw new ArgumentException($"Read size too big, should be less than {MaxPageSize} items");
			}

			var streamName = msg.EventStreamId;
			var streamId = _readIndex.GetStreamId(msg.EventStreamId);
			if (msg.ValidationStreamVersion.HasValue &&
				await _readIndex.GetStreamLastEventNumber(streamId, token) == msg.ValidationStreamVersion)
				return NoData(ReadStreamResult.NotModified, lastIndexPosition, msg.ValidationStreamVersion.Value);

			var result = await _readIndex.ReadStreamEventsForward(streamName, streamId, msg.FromEventNumber, msg.MaxCount, token);
			CheckEventsOrder(msg, result);
			if (await ResolveLinkToEvents(result.Records, msg.ResolveLinkTos, msg.User, token) is not { } resolvedPairs)
				return NoData(ReadStreamResult.AccessDenied, lastIndexPosition);

			return new ReadStreamEventsForwardCompleted(
				msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount,
				(ReadStreamResult)result.Result, resolvedPairs, result.Metadata, false, string.Empty,
				result.NextEventNumber, result.LastEventNumber, result.IsEndOfStream, lastIndexPosition);
		} catch (Exception exc) when (exc is not OperationCanceledException oce || oce.CancellationToken != token) {
			Log.Error(exc, "Error during processing ReadStreamEventsForward request.");
			return NoData(ReadStreamResult.Error, lastIndexPosition, error: exc.Message);
		}

		static void CheckEventsOrder(ReadStreamEventsForward msg, IndexReadStreamResult result) {
			for (var index = 1; index < result.Records.Length; index++) {
				if (result.Records[index].EventNumber != result.Records[index - 1].EventNumber + 1) {
					throw new Exception(
						$"Invalid order of events has been detected in read index for the event stream '{msg.EventStreamId}'. " +
						$"The event {result.Records[index].EventNumber} at position {result.Records[index].LogPosition} goes after the event {result.Records[index - 1].EventNumber} at position {result.Records[index - 1].LogPosition}");
				}
			}
		}

		ReadStreamEventsForwardCompleted NoData(ReadStreamResult result, long lastIndexedPosition, long lastEventNumber = -1, long nextEventNumber = -1, string error = null)
			=> new(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, result,
				EmptyRecords, null, false, error ?? string.Empty, nextEventNumber, lastEventNumber, true, lastIndexedPosition);
	}

	private async ValueTask<ReadStreamEventsBackwardCompleted> ReadStreamEventsBackward(ReadStreamEventsBackward msg, CancellationToken token) {
		var lastIndexedPosition = _readIndex.LastIndexedPosition;
		try {
			if (msg.MaxCount > MaxPageSize) {
				throw new ArgumentException($"Read size too big, should be less than {MaxPageSize} items");
			}

			var streamName = msg.EventStreamId;
			var streamId = _readIndex.GetStreamId(msg.EventStreamId);
			if (msg.ValidationStreamVersion.HasValue &&
				await _readIndex.GetStreamLastEventNumber(streamId, token) == msg.ValidationStreamVersion)
				return NoData(ReadStreamResult.NotModified, msg.ValidationStreamVersion.Value);

			var result = await _readIndex.ReadStreamEventsBackward(streamName, streamId, msg.FromEventNumber, msg.MaxCount, token);
			CheckEventsOrder(msg, result);
			if (await ResolveLinkToEvents(result.Records, msg.ResolveLinkTos, msg.User, token) is not { } resolvedPairs)
				return NoData(ReadStreamResult.AccessDenied);

			return new ReadStreamEventsBackwardCompleted(
				msg.CorrelationId, msg.EventStreamId, result.FromEventNumber, result.MaxCount,
				(ReadStreamResult)result.Result, resolvedPairs, result.Metadata, false, string.Empty,
				result.NextEventNumber, result.LastEventNumber, result.IsEndOfStream, lastIndexedPosition);
		} catch (Exception exc) when (exc is not OperationCanceledException oce || oce.CancellationToken != token) {
			Log.Error(exc, "Error during processing ReadStreamEventsBackward request.");
			return NoData(ReadStreamResult.Error, error: exc.Message);
		}

		static void CheckEventsOrder(ReadStreamEventsBackward msg, IndexReadStreamResult result) {
			for (var index = 1; index < result.Records.Length; index++) {
				if (result.Records[index].EventNumber != result.Records[index - 1].EventNumber - 1) {
					throw new Exception(
						$"Invalid order of events has been detected in read index for the event stream '{msg.EventStreamId}'. " +
						$"The event {result.Records[index].EventNumber} at position {result.Records[index].LogPosition} goes after the event {result.Records[index - 1].EventNumber} at position {result.Records[index - 1].LogPosition}");
				}
			}
		}

		ReadStreamEventsBackwardCompleted NoData(ReadStreamResult result, long lastEventNumber = -1, long nextEventNumber = -1, string error = null)
			=> new(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, result,
				EmptyRecords, null, false, error ?? string.Empty, nextEventNumber, lastEventNumber, true, lastIndexedPosition);
	}
}
