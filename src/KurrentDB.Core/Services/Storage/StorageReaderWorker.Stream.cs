// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using static KurrentDB.Core.Messages.ClientMessage;
using static KurrentDB.Core.Messages.SubscriptionMessage;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;

namespace KurrentDB.Core.Services.Storage;

partial class StorageReaderWorker<TStreamId> : IAsyncHandle<ReadStreamEventsBackward>,
	IAsyncHandle<ReadStreamEventsForward> {
	async ValueTask IAsyncHandle<ReadStreamEventsForward>.HandleAsync(ReadStreamEventsForward msg, CancellationToken token) {
		ReadStreamEventsForwardCompleted res;
		var lastIndexPosition = _readIndex.LastIndexedPosition;
		var cts = _multiplexer.Combine(msg.Lifetime, [token, msg.CancellationToken]);
		var leaseTaken = false;
		try {
			await AcquireRateLimitLeaseAsync(cts.Token);
			leaseTaken = true;

			res = await ReadStreamEventsForward(msg, lastIndexPosition, cts.Token);
		} catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token) {
			if (!cts.IsTimedOut)
				throw new OperationCanceledException(null, ex, cts.CancellationOrigin);

			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new ReadStreamEventsForwardCompleted(
					msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, ReadStreamResult.Expired,
					ResolvedEvent.EmptyArray, default, default, default, -1, default, true, default));
			}

			if (LogExpiredMessage())
				Log.Debug(
					"Read Stream Events Forward operation has expired for Stream: {stream}, From Event Number: {fromEventNumber}, Max Count: {maxCount}. Operation Expired at {expiryDateTime} after {lifetime:N0} ms.",
					msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		} catch (Exception exc) {
			Log.Error(exc, "Error during processing ReadStreamEventsForward request.");
			res = msg.NoData(ReadStreamResult.Error, lastIndexPosition, error: exc.Message);
		} finally {
			await cts.DisposeAsync();

			if (leaseTaken)
				ReleaseRateLimitLease();
		}

		switch (res.Result) {
			case ReadStreamResult.Success
				or ReadStreamResult.NoStream
				or ReadStreamResult.NotModified
				when msg.LongPollTimeout is { } longPollTimeout && res.FromEventNumber > res.LastEventNumber:
				_publisher.Publish(new PollStream(
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

	async ValueTask IAsyncHandle<ReadStreamEventsBackward>.HandleAsync(
		ReadStreamEventsBackward msg, CancellationToken token) {
		ReadStreamEventsBackwardCompleted res;
		var lastIndexedPosition = _readIndex.LastIndexedPosition;
		var cts = _multiplexer.Combine(msg.Lifetime, [token, msg.CancellationToken]);
		var leaseTaken = false;
		try {
			await AcquireRateLimitLeaseAsync(cts.Token);
			leaseTaken = true;

			res = await ReadStreamEventsBackward(msg, lastIndexedPosition, cts.Token);
		} catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token) {
			if (!cts.IsTimedOut)
				throw new OperationCanceledException(null, ex, cts.CancellationOrigin);

			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new ReadStreamEventsBackwardCompleted(
					msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, ReadStreamResult.Expired,
					ResolvedEvent.EmptyArray, default, default, default, -1, default, true, default));
			}

			if (LogExpiredMessage())
				Log.Debug(
					"Read Stream Events Backward operation has expired for Stream: {stream}, From Event Number: {fromEventNumber}, Max Count: {maxCount}. Operation Expired at {expiryDateTime} after {lifetime:N0} ms.",
					msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		} catch (Exception exc) {
			Log.Error(exc, "Error during processing ReadStreamEventsBackward request.");
			res = msg.NoData(ReadStreamResult.Error, lastIndexedPosition, error: exc.Message);
		} finally {
			await cts.DisposeAsync();

			if (leaseTaken)
				ReleaseRateLimitLease();
		}

		msg.Envelope.ReplyWith(res);
	}

	private async ValueTask<ReadStreamEventsForwardCompleted> ReadStreamEventsForward(ReadStreamEventsForward msg,
		long lastIndexPosition,
		CancellationToken token) {
		if (SystemStreams.IsInMemoryStream(msg.EventStreamId))
			return await _virtualStreamReader.ReadForwards(msg, token);

		if (msg.MaxCount > MaxPageSize) {
			throw new ArgumentException($"Read size too big, should be less than {MaxPageSize} items");
		}

		var streamName = msg.EventStreamId;
		var streamId = _readIndex.GetStreamId(msg.EventStreamId);
		if (msg.ValidationStreamVersion is { } streamVer &&
		    await _readIndex.GetStreamLastEventNumber(streamId, token) == streamVer)
			return msg.NoData(ReadStreamResult.NotModified, lastIndexPosition, streamVer);

		var result = await _readIndex.ReadStreamEventsForward(streamName, streamId, msg.FromEventNumber, msg.MaxCount, token);
		CheckEventsOrder(msg, result);
		if (await ResolveLinkToEvents(result.Records, msg.ResolveLinkTos, msg.User, token) is not { } resolvedPairs)
			return msg.NoData(ReadStreamResult.AccessDenied, lastIndexPosition);

		return new(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount,
			(ReadStreamResult)result.Result, resolvedPairs, result.Metadata, false, string.Empty,
			result.NextEventNumber, result.LastEventNumber, result.IsEndOfStream, lastIndexPosition);

		static void CheckEventsOrder(ReadStreamEventsForward msg, IndexReadStreamResult result) {
			for (var index = 1; index < result.Records.Length; index++) {
				if (result.Records[index].EventNumber != result.Records[index - 1].EventNumber + 1) {
					throw new Exception(
						$"Invalid order of events has been detected in read index for the event stream '{msg.EventStreamId}'. " +
						$"The event {result.Records[index].EventNumber} at position {result.Records[index].LogPosition} goes after the event {result.Records[index - 1].EventNumber} at position {result.Records[index - 1].LogPosition}");
				}
			}
		}
	}

	private async ValueTask<ReadStreamEventsBackwardCompleted> ReadStreamEventsBackward(ReadStreamEventsBackward msg,
		long lastIndexedPosition,
		CancellationToken token) {
		if (SystemStreams.IsInMemoryStream(msg.EventStreamId))
			return await _virtualStreamReader.ReadBackwards(msg, token);

		if (msg.MaxCount > MaxPageSize) {
			throw new ArgumentException($"Read size too big, should be less than {MaxPageSize} items");
		}

		var streamName = msg.EventStreamId;
		var streamId = _readIndex.GetStreamId(msg.EventStreamId);
		if (msg.ValidationStreamVersion is { } streamVer &&
		    await _readIndex.GetStreamLastEventNumber(streamId, token) == streamVer)
			return msg.NoData(ReadStreamResult.NotModified, lastIndexedPosition, streamVer);

		var result = await _readIndex.ReadStreamEventsBackward(streamName, streamId, msg.FromEventNumber, msg.MaxCount, token);
		CheckEventsOrder(msg, result);
		if (await ResolveLinkToEvents(result.Records, msg.ResolveLinkTos, msg.User, token) is not { } resolvedPairs)
			return msg.NoData(ReadStreamResult.AccessDenied, lastIndexedPosition);

		return new(msg.CorrelationId, msg.EventStreamId, result.FromEventNumber, result.MaxCount,
			(ReadStreamResult)result.Result, resolvedPairs, result.Metadata, false, string.Empty,
			result.NextEventNumber, result.LastEventNumber, result.IsEndOfStream, lastIndexedPosition);

		static void CheckEventsOrder(ReadStreamEventsBackward msg, IndexReadStreamResult result) {
			for (var index = 1; index < result.Records.Length; index++) {
				if (result.Records[index].EventNumber != result.Records[index - 1].EventNumber - 1) {
					throw new Exception(
						$"Invalid order of events has been detected in read index for the event stream '{msg.EventStreamId}'. " +
						$"The event {result.Records[index].EventNumber} at position {result.Records[index].LogPosition} goes after the event {result.Records[index - 1].EventNumber} at position {result.Records[index - 1].LogPosition}");
				}
			}
		}
	}
}

file static class EmptyDataProvider {
	public static ReadStreamEventsBackwardCompleted NoData(this ReadStreamEventsBackward msg,
		ReadStreamResult result,
		long lastIndexedPosition,
		long lastEventNumber = -1,
		long nextEventNumber = -1,
		string error = null)
		=> new(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, result,
			[], null, false, error ?? string.Empty, nextEventNumber, lastEventNumber, true, lastIndexedPosition);

	public static ReadStreamEventsForwardCompleted NoData(this ReadStreamEventsForward msg,
		ReadStreamResult result,
		long lastIndexedPosition,
		long lastEventNumber = -1,
		long nextEventNumber = -1,
		string error = null)
		=> new(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount, result,
			[], null, false, error ?? string.Empty, nextEventNumber, lastEventNumber, true, lastIndexedPosition);
}
