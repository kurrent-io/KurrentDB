// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Exceptions;
using KurrentDB.Core.Messages;
using KurrentDB.Core.TransactionLog.Chunks.TFChunk;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.Core.Services.Storage;

public partial class StorageReaderWorker<TStreamId> {
	async ValueTask IAsyncHandle<FilteredReadAllEventsForward>.HandleAsync(FilteredReadAllEventsForward msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

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
			case FilteredReadAllResult.Success:
				if (msg.LongPollTimeout.HasValue && res.IsEndOfStream && res.Events.Count is 0) {
					_publisher.Publish(new SubscriptionMessage.PollStream(
						SubscriptionsService.AllStreamsSubscriptionId, res.TfLastCommitPosition, null,
						DateTime.UtcNow + msg.LongPollTimeout.Value, msg));
				} else
					msg.Envelope.ReplyWith(res);

				break;
			case FilteredReadAllResult.NotModified:
				if (msg.LongPollTimeout.HasValue && res.IsEndOfStream && res.CurrentPos.CommitPosition > res.TfLastCommitPosition) {
					_publisher.Publish(new SubscriptionMessage.PollStream(
						SubscriptionsService.AllStreamsSubscriptionId, res.TfLastCommitPosition, null,
						DateTime.UtcNow + msg.LongPollTimeout.Value, msg));
				} else
					msg.Envelope.ReplyWith(res);

				break;
			case FilteredReadAllResult.Error:
			case FilteredReadAllResult.AccessDenied:
			case FilteredReadAllResult.InvalidPosition:
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
			case FilteredReadAllResult.Success:
				if (msg.LongPollTimeout.HasValue && res.IsEndOfStream && res.Events.Count is 0) {
					_publisher.Publish(new SubscriptionMessage.PollStream(
						SubscriptionsService.AllStreamsSubscriptionId, res.TfLastCommitPosition, null,
						DateTime.UtcNow + msg.LongPollTimeout.Value, msg));
				} else
					msg.Envelope.ReplyWith(res);

				break;
			case FilteredReadAllResult.NotModified:
				if (msg.LongPollTimeout.HasValue && res.IsEndOfStream && res.CurrentPos.CommitPosition > res.TfLastCommitPosition) {
					_publisher.Publish(new SubscriptionMessage.PollStream(
						SubscriptionsService.AllStreamsSubscriptionId, res.TfLastCommitPosition, null,
						DateTime.UtcNow + msg.LongPollTimeout.Value, msg));
				} else
					msg.Envelope.ReplyWith(res);

				break;
			case FilteredReadAllResult.Error:
			case FilteredReadAllResult.AccessDenied:
			case FilteredReadAllResult.InvalidPosition:
				msg.Envelope.ReplyWith(res);
				break;
			default:
				throw new ArgumentOutOfRangeException($"Unknown ReadAllResult: {res.Result}");
		}
	}

	private async ValueTask<FilteredReadAllEventsForwardCompleted> FilteredReadAllEventsForward(FilteredReadAllEventsForward msg, CancellationToken token) {
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
			return new FilteredReadAllEventsForwardCompleted(
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

		FilteredReadAllEventsForwardCompleted NoData(FilteredReadAllResult result, string error = null)
			=> new(msg.CorrelationId, result, error, ResolvedEvent.EmptyArray, null, false,
				msg.MaxCount, pos, TFPos.Invalid, TFPos.Invalid, lastIndexedPosition, false, 0L);
	}

	private async ValueTask<FilteredReadAllEventsBackwardCompleted> FilteredReadAllEventsBackward(FilteredReadAllEventsBackward msg, CancellationToken token) {
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
			return new FilteredReadAllEventsBackwardCompleted(
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

		FilteredReadAllEventsBackwardCompleted NoData(FilteredReadAllResult result, string error = null)
			=> new(msg.CorrelationId, result, error, ResolvedEvent.EmptyArray, null, false,
				msg.MaxCount, pos, TFPos.Invalid, TFPos.Invalid, lastIndexedPosition, false);
	}
}
