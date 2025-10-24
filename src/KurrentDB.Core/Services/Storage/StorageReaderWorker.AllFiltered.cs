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

partial class StorageReaderWorker<TStreamId> : IAsyncHandle<FilteredReadAllEventsForward>,
	IAsyncHandle<FilteredReadAllEventsBackward> {
	async ValueTask IAsyncHandle<FilteredReadAllEventsForward>.HandleAsync(FilteredReadAllEventsForward msg, CancellationToken token) {
		FilteredReadAllEventsForwardCompleted res;
		var pos = new TFPos(msg.CommitPosition, msg.PreparePosition);
		var lastIndexedPosition = _readIndex.LastIndexedPosition;
		var cts = _multiplexer.Combine(msg.Lifetime, [token, msg.CancellationToken]);
		try {
			res = await FilteredReadAllEventsForward(msg, pos, lastIndexedPosition, cts.Token);
		} catch (OperationCanceledException e) when (e.CancellationToken == cts.Token) {
			if (!cts.IsTimedOut)
				throw new OperationCanceledException(null, e, cts.CancellationOrigin);

			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new FilteredReadAllEventsForwardCompleted(
					msg.CorrelationId, FilteredReadAllResult.Expired,
					default, ResolvedEvent.EmptyArray, default, default, default,
					currentPos: new TFPos(msg.CommitPosition, msg.PreparePosition),
					TFPos.Invalid, TFPos.Invalid, default, default, default));
			}

			Log.Debug(
				"Read All Stream Events Forward Filtered operation has expired for C:{0}/P:{1}. Operation Expired at {2} after {lifetime:N0} ms.",
				msg.CommitPosition, msg.PreparePosition, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		} catch (Exception exc) when (exc is InvalidReadException or UnableToReadPastEndOfStreamException) {
			Log.Warning(exc,
				"Error during processing ReadAllEventsForwardFiltered request. The read appears to be at an invalid position.");
			res = msg.NoData(FilteredReadAllResult.InvalidPosition, pos, lastIndexedPosition, exc.Message);
		} catch (Exception exc) {
			Log.Error(exc, "Error during processing ReadAllEventsForwardFiltered request.");
			res = msg.NoData(FilteredReadAllResult.Error, pos, lastIndexedPosition, exc.Message);
		} finally {
			await cts.DisposeAsync();
		}

		switch (res.Result) {
			case FilteredReadAllResult.Success
				when msg.LongPollTimeout is { } longPollTimeout && res is { IsEndOfStream: true, Events: [] }:
			case FilteredReadAllResult.NotModified
				when msg.LongPollTimeout.TryGetValue(out longPollTimeout)
				     && res.IsEndOfStream
				     && res.CurrentPos.CommitPosition > res.TfLastCommitPosition:
				_publisher.Publish(new PollStream(
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

	async ValueTask IAsyncHandle<FilteredReadAllEventsBackward>.HandleAsync(FilteredReadAllEventsBackward msg, CancellationToken token) {
		FilteredReadAllEventsBackwardCompleted res;
		var pos = new TFPos(msg.CommitPosition, msg.PreparePosition);
		var lastIndexedPosition = _readIndex.LastIndexedPosition;
		var cts = _multiplexer.Combine(msg.Lifetime, [token, msg.CancellationToken]);
		try {
			res = await FilteredReadAllEventsBackward(msg, pos, lastIndexedPosition, cts.Token);
		} catch (OperationCanceledException e) when (e.CancellationToken == cts.Token) {
			if (!cts.IsTimedOut)
				throw new OperationCanceledException(null, e, cts.CancellationOrigin);

			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new FilteredReadAllEventsBackwardCompleted(
					msg.CorrelationId, FilteredReadAllResult.Expired,
					default, ResolvedEvent.EmptyArray, default, default, default,
					currentPos: new TFPos(msg.CommitPosition, msg.PreparePosition),
					TFPos.Invalid, TFPos.Invalid, default, default));
			}

			Log.Debug(
				"Read All Stream Events Backward Filtered operation has expired for C:{0}/P:{1}. Operation Expired at {2} after {lifetime:N0} ms.",
				msg.CommitPosition, msg.PreparePosition, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		} catch (Exception exc) when (exc is InvalidReadException or UnableToReadPastEndOfStreamException) {
			Log.Warning(exc,
				"Error during processing ReadAllEventsBackwardFiltered request. The read appears to be at an invalid position.");
			res = msg.NoData(FilteredReadAllResult.InvalidPosition, pos, lastIndexedPosition, exc.Message);
		} catch (Exception exc) {
			Log.Error(exc, "Error during processing ReadAllEventsBackwardFiltered request.");
			res = msg.NoData(FilteredReadAllResult.Error, pos, lastIndexedPosition, exc.Message);
		} finally {
			await cts.DisposeAsync();
		}

		switch (res.Result) {
			case FilteredReadAllResult.Success
				when msg.LongPollTimeout is { } longPollTimeout && res is { IsEndOfStream: true, Events: [] }:
			case FilteredReadAllResult.NotModified
				when msg.LongPollTimeout.TryGetValue(out longPollTimeout)
				     && res.IsEndOfStream
				     && res.CurrentPos.CommitPosition > res.TfLastCommitPosition:
				_publisher.Publish(new PollStream(
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

	private async ValueTask<FilteredReadAllEventsForwardCompleted> FilteredReadAllEventsForward(FilteredReadAllEventsForward msg,
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
			return msg.NoData(FilteredReadAllResult.InvalidPosition, pos, lastIndexedPosition, "Invalid position.");
		if (msg.ValidationTfLastCommitPosition == lastIndexedPosition)
			return msg.NoData(FilteredReadAllResult.NotModified, pos, lastIndexedPosition);

		var res = await _readIndex.ReadAllEventsForwardFiltered(pos, msg.MaxCount, msg.MaxSearchWindow, msg.EventFilter, token);
		if (await ResolveReadAllResult(res.Records, msg.ResolveLinkTos, msg.User, token) is not { } resolved)
			return msg.NoData(FilteredReadAllResult.AccessDenied, pos, lastIndexedPosition);

		var metadata = await _readIndex.GetStreamMetadata(_systemStreams.AllStream, token);
		return new FilteredReadAllEventsForwardCompleted(
			msg.CorrelationId, FilteredReadAllResult.Success, null, resolved, metadata, false,
			msg.MaxCount, res.CurrentPos, res.NextPos, res.PrevPos, lastIndexedPosition, res.IsEndOfStream,
			res.ConsideredEventsCount);
	}

	private async ValueTask<FilteredReadAllEventsBackwardCompleted> FilteredReadAllEventsBackward(FilteredReadAllEventsBackward msg,
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
			return msg.NoData(FilteredReadAllResult.InvalidPosition, pos, lastIndexedPosition, "Invalid position.");
		if (msg.ValidationTfLastCommitPosition == lastIndexedPosition)
			return msg.NoData(FilteredReadAllResult.NotModified, pos, lastIndexedPosition);

		var res = await _readIndex.ReadAllEventsBackwardFiltered(pos, msg.MaxCount, msg.MaxSearchWindow,
			msg.EventFilter, token);
		if (await ResolveReadAllResult(res.Records, msg.ResolveLinkTos, msg.User, token) is not { } resolved)
			return msg.NoData(FilteredReadAllResult.AccessDenied, pos, lastIndexedPosition);

		var metadata = await _readIndex.GetStreamMetadata(_systemStreams.AllStream, token);
		return new FilteredReadAllEventsBackwardCompleted(
			msg.CorrelationId, FilteredReadAllResult.Success, null, resolved, metadata, false,
			msg.MaxCount,
			res.CurrentPos, res.NextPos, res.PrevPos, lastIndexedPosition, res.IsEndOfStream);
	}
}

file static class EmptyDataProvider {
	public static FilteredReadAllEventsBackwardCompleted NoData(this FilteredReadAllEventsBackward msg,
		FilteredReadAllResult result,
		TFPos pos,
		long lastIndexedPosition,
		string error = null)
		=> new(msg.CorrelationId, result, error, ResolvedEvent.EmptyArray, null, false,
			msg.MaxCount, pos, TFPos.Invalid, TFPos.Invalid, lastIndexedPosition, false);

	public static FilteredReadAllEventsForwardCompleted NoData(this FilteredReadAllEventsForward msg,
		FilteredReadAllResult result,
		TFPos pos,
		long lastIndexedPosition,
		string error = null)
		=> new(msg.CorrelationId, result, error, ResolvedEvent.EmptyArray, null, false,
			msg.MaxCount, pos, TFPos.Invalid, TFPos.Invalid, lastIndexedPosition, false, 0L);
}
