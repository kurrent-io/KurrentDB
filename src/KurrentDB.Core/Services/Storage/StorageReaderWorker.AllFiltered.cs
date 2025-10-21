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
		if (msg.Expires < DateTime.UtcNow) {
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
		}

		var res = await FilteredReadAllEventsForward(msg, token);
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
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
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
		}

		var res = await FilteredReadAllEventsBackward(msg, token);
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

	private async ValueTask<FilteredReadAllEventsForwardCompleted> FilteredReadAllEventsForward(FilteredReadAllEventsForward msg, CancellationToken token) {
		var pos = new TFPos(msg.CommitPosition, msg.PreparePosition);
		var lastIndexedPosition = _readIndex.LastIndexedPosition;
		var cts = _multiplexer.Combine([token, msg.CancellationToken]);
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

			var res = await _readIndex.ReadAllEventsForwardFiltered(pos, msg.MaxCount, msg.MaxSearchWindow, msg.EventFilter, cts.Token);
			if (await ResolveReadAllResult(res.Records, msg.ResolveLinkTos, msg.User, cts.Token) is not { } resolved)
				return NoData(FilteredReadAllResult.AccessDenied);

			var metadata = await _readIndex.GetStreamMetadata(_systemStreams.AllStream, cts.Token);
			return new FilteredReadAllEventsForwardCompleted(
				msg.CorrelationId, FilteredReadAllResult.Success, null, resolved, metadata, false,
				msg.MaxCount, res.CurrentPos, res.NextPos, res.PrevPos, lastIndexedPosition, res.IsEndOfStream,
				res.ConsideredEventsCount);
		} catch (OperationCanceledException e) when (e.CancellationToken == cts.Token) {
			throw new OperationCanceledException(null, e, cts.CancellationOrigin);
		} catch (Exception exc) when (exc is InvalidReadException or UnableToReadPastEndOfStreamException) {
			Log.Warning(exc,
				"Error during processing ReadAllEventsForwardFiltered request. The read appears to be at an invalid position.");
			return NoData(FilteredReadAllResult.InvalidPosition, exc.Message);
		} catch (Exception exc) {
			Log.Error(exc, "Error during processing ReadAllEventsForwardFiltered request.");
			return NoData(FilteredReadAllResult.Error, exc.Message);
		} finally {
			await cts.DisposeAsync();
		}

		FilteredReadAllEventsForwardCompleted NoData(FilteredReadAllResult result, string error = null)
			=> new(msg.CorrelationId, result, error, ResolvedEvent.EmptyArray, null, false,
				msg.MaxCount, pos, TFPos.Invalid, TFPos.Invalid, lastIndexedPosition, false, 0L);
	}

	private async ValueTask<FilteredReadAllEventsBackwardCompleted> FilteredReadAllEventsBackward(FilteredReadAllEventsBackward msg, CancellationToken token) {
		var pos = new TFPos(msg.CommitPosition, msg.PreparePosition);
		var lastIndexedPosition = _readIndex.LastIndexedPosition;
		var cts = _multiplexer.Combine([token, msg.CancellationToken]);
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
				msg.EventFilter, cts.Token);
			if (await ResolveReadAllResult(res.Records, msg.ResolveLinkTos, msg.User, cts.Token) is not { } resolved)
				return NoData(FilteredReadAllResult.AccessDenied);

			var metadata = await _readIndex.GetStreamMetadata(_systemStreams.AllStream, cts.Token);
			return new FilteredReadAllEventsBackwardCompleted(
				msg.CorrelationId, FilteredReadAllResult.Success, null, resolved, metadata, false,
				msg.MaxCount,
				res.CurrentPos, res.NextPos, res.PrevPos, lastIndexedPosition, res.IsEndOfStream);
		} catch (OperationCanceledException e) when (e.CancellationToken == cts.Token) {
			throw new OperationCanceledException(null, e, cts.CancellationOrigin);
		} catch (Exception exc) when (exc is InvalidReadException or UnableToReadPastEndOfStreamException) {
			Log.Warning(exc,
				"Error during processing ReadAllEventsBackwardFiltered request. The read appears to be at an invalid position.");
			return NoData(FilteredReadAllResult.InvalidPosition, exc.Message);
		} catch (Exception exc) {
			Log.Error(exc, "Error during processing ReadAllEventsBackwardFiltered request.");
			return NoData(FilteredReadAllResult.Error, exc.Message);
		} finally {
			await cts.DisposeAsync();
		}

		FilteredReadAllEventsBackwardCompleted NoData(FilteredReadAllResult result, string error = null)
			=> new(msg.CorrelationId, result, error, ResolvedEvent.EmptyArray, null, false,
				msg.MaxCount, pos, TFPos.Invalid, TFPos.Invalid, lastIndexedPosition, false);
	}
}
