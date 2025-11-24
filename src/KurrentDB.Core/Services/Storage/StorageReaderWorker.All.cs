// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Exceptions;
using KurrentDB.Core.TransactionLog.Chunks.TFChunk;
using static KurrentDB.Core.Messages.ClientMessage;
using static KurrentDB.Core.Messages.SubscriptionMessage;

namespace KurrentDB.Core.Services.Storage;

partial class StorageReaderWorker<TStreamId> : IAsyncHandle<ReadAllEventsForward>,
	IAsyncHandle<ReadAllEventsBackward> {

	async ValueTask IAsyncHandle<ReadAllEventsForward>.HandleAsync(ReadAllEventsForward msg, CancellationToken token) {
		var pos = new TFPos(msg.CommitPosition, msg.PreparePosition);
		var lastIndexedPosition = _readIndex.LastIndexedPosition;
		ReadAllEventsForwardCompleted res;
		var cts = _multiplexer.Combine(msg.Lifetime, [token, msg.CancellationToken]);
		var leaseTaken = false;
		try {
			await AcquireRateLimitLeaseAsync(cts.Token);
			leaseTaken = true;

			res = await ReadAllEventsForward(msg, pos, lastIndexedPosition, cts.Token);
		} catch (OperationCanceledException e) when (e.CancellationToken == cts.Token) {
			if (!cts.IsTimedOut)
				throw new OperationCanceledException(null, e, cts.CancellationOrigin);

			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new ReadAllEventsForwardCompleted(
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
		} catch (Exception exc) when (exc is InvalidReadException or UnableToReadPastEndOfStreamException) {
			Log.Warning(exc, "Error during processing ReadAllEventsBackward request. The read appears to be at an invalid position.");
			res = msg.NoData(ReadAllResult.InvalidPosition, pos, lastIndexedPosition, exc.Message);
		} catch (Exception exc) {
			Log.Error(exc, "Error during processing ReadAllEventsForward request.");
			res = msg.NoData(ReadAllResult.Error, pos, lastIndexedPosition, exc.Message);
		} finally {
			await cts.DisposeAsync();

			if (leaseTaken)
				ReleaseRateLimitLease();
		}

		switch (res.Result) {
			case ReadAllResult.Success when msg.LongPollTimeout is { } longPoolTimeout && res is { IsEndOfStream: true, Events: [] }:
			case ReadAllResult.NotModified when msg.LongPollTimeout.TryGetValue(out longPoolTimeout)
			                                    && res.IsEndOfStream
			                                    && res.CurrentPos.CommitPosition > res.TfLastCommitPosition:
				_publisher.Publish(new PollStream(
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

	async ValueTask IAsyncHandle<ReadAllEventsBackward>.HandleAsync(ReadAllEventsBackward msg, CancellationToken token) {
		var pos = new TFPos(msg.CommitPosition, msg.PreparePosition);
		var lastIndexedPosition = _readIndex.LastIndexedPosition;
		ReadAllEventsBackwardCompleted res;
		var cts = _multiplexer.Combine(msg.Lifetime, [token, msg.CancellationToken]);
		var leaseTaken = false;
		try {
			await AcquireRateLimitLeaseAsync(cts.Token);
			leaseTaken = true;

			res = await ReadAllEventsBackward(msg, pos, lastIndexedPosition, cts.Token);
		} catch (OperationCanceledException e) when (e.CancellationToken == cts.Token) {
			if (!cts.IsTimedOut)
				throw new OperationCanceledException(null, e, cts.CancellationOrigin);

			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new ReadAllEventsBackwardCompleted(
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
		} catch (Exception exc) when (exc is InvalidReadException or UnableToReadPastEndOfStreamException) {
			Log.Warning(exc, "Error during processing ReadAllEventsBackward request. The read appears to be at an invalid position.");
			res = msg.NoData(ReadAllResult.InvalidPosition, pos, lastIndexedPosition, exc.Message);
		} catch (Exception exc) {
			Log.Error(exc, "Error during processing ReadAllEventsBackward request.");
			res = msg.NoData(ReadAllResult.Error, pos, lastIndexedPosition, exc.Message);
		} finally {
			await cts.DisposeAsync();

			if (leaseTaken)
				ReleaseRateLimitLease();
		}

		msg.Envelope.ReplyWith(res);
	}

	private async ValueTask<ReadAllEventsForwardCompleted> ReadAllEventsForward(ReadAllEventsForward msg,
		TFPos pos,
		long lastIndexedPosition,
		CancellationToken token) {
		if (msg.MaxCount > MaxPageSize) {
			throw new ArgumentException($"Read size too big, should be less than {MaxPageSize} items");
		}

		if (pos == TFPos.HeadOfTf) {
			var checkpoint = _writerCheckpoint.Read();
			pos = new TFPos(checkpoint, checkpoint);
		}

		if (pos.CommitPosition < 0 || pos.PreparePosition < 0)
			return msg.NoData(ReadAllResult.InvalidPosition, pos, lastIndexedPosition, "Invalid position.");
		if (msg.ValidationTfLastCommitPosition == lastIndexedPosition)
			return msg.NoData(ReadAllResult.NotModified, pos, lastIndexedPosition);

		var res = await _readIndex.ReadAllEventsForward(pos, msg.MaxCount, token);
		if (await ResolveReadAllResult(res.Records, msg.ResolveLinkTos, msg.User, token) is not { } resolved)
			return msg.NoData(ReadAllResult.AccessDenied, pos, lastIndexedPosition);

		var metadata = await _readIndex.GetStreamMetadata(_systemStreams.AllStream, token);
		return new(msg.CorrelationId, ReadAllResult.Success, null, resolved, metadata,
			false, msg.MaxCount, res.CurrentPos, res.NextPos, res.PrevPos, lastIndexedPosition);
	}

	private async ValueTask<ReadAllEventsBackwardCompleted> ReadAllEventsBackward(ReadAllEventsBackward msg,
		TFPos pos,
		long lastIndexedPosition,
		CancellationToken token) {
		if (msg.MaxCount > MaxPageSize) {
			throw new ArgumentException($"Read size too big, should be less than {MaxPageSize} items");
		}

		if (pos == TFPos.HeadOfTf) {
			var checkpoint = _writerCheckpoint.Read();
			pos = new TFPos(checkpoint, checkpoint);
		}

		if (pos.CommitPosition < 0 || pos.PreparePosition < 0)
			return msg.NoData(ReadAllResult.InvalidPosition, pos, lastIndexedPosition, "Invalid position.");
		if (msg.ValidationTfLastCommitPosition == lastIndexedPosition)
			return msg.NoData(ReadAllResult.NotModified, pos, lastIndexedPosition);

		var res = await _readIndex.ReadAllEventsBackward(pos, msg.MaxCount, token);
		if (await ResolveReadAllResult(res.Records, msg.ResolveLinkTos, msg.User, token) is not { } resolved)
			return msg.NoData(ReadAllResult.AccessDenied, pos, lastIndexedPosition);

		var metadata = await _readIndex.GetStreamMetadata(_systemStreams.AllStream, token);
		return new(msg.CorrelationId, ReadAllResult.Success, null, resolved, metadata,
			false, msg.MaxCount, res.CurrentPos, res.NextPos, res.PrevPos, lastIndexedPosition);
	}
}

file static class EmptyDataProvider {
	public static ReadAllEventsBackwardCompleted NoData(this ReadAllEventsBackward msg,
		ReadAllResult result,
		TFPos pos,
		long lastIndexedPosition,
		string error = null)
		=> new(msg.CorrelationId, result, error, ResolvedEvent.EmptyArray, null, false,
			msg.MaxCount, pos, TFPos.Invalid, TFPos.Invalid, lastIndexedPosition);

	public static ReadAllEventsForwardCompleted NoData(this ReadAllEventsForward msg,
		ReadAllResult result,
		TFPos pos,
		long lastIndexedPosition,
		string error = null)
		=> new(msg.CorrelationId, result, error, ResolvedEvent.EmptyArray, null, false,
			msg.MaxCount, pos, TFPos.Invalid, TFPos.Invalid, lastIndexedPosition);
}
