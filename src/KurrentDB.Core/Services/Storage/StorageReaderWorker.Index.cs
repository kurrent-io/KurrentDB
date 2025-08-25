// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;

namespace KurrentDB.Core.Services.Storage;

public partial class StorageReaderWorker<TStreamId> :
	IAsyncHandle<ClientMessage.ReadIndexEventsForward>,
	IAsyncHandle<ClientMessage.ReadIndexEventsBackward> {
	public async ValueTask HandleAsync(ClientMessage.ReadIndexEventsForward msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(
					new ClientMessage.ReadIndexEventsForwardCompleted(
						ReadIndexResult.Expired,
						ResolvedEvent.EmptyArray,
						new(msg.CommitPosition, msg.PreparePosition),
						0,
						false,
						null
					)
				);
			}

			Log.Debug(
				"ReadIndexEventsForward operation has expired for C:{CommitPosition}/P:{PreparePosition}. Operation expired at {ExpiredAt} after {lifetime:N0} ms.",
				msg.CommitPosition, msg.PreparePosition, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		}

		var res = await _secondaryIndexReaders.ReadForwards(msg, token);
		switch (res.Result) {
			case ReadIndexResult.Success:
				if (msg.LongPollTimeout.HasValue && res.IsEndOfStream && res.Events.Count is 0) {
					PublishLongPoll();
				} else {
					msg.Envelope.ReplyWith(res);
				}

				break;
			case ReadIndexResult.NotModified:
			case ReadIndexResult.Error:
			case ReadIndexResult.InvalidPosition:
			case ReadIndexResult.IndexNotFound:
				msg.Envelope.ReplyWith(res);
				break;
			default:
				throw new ArgumentOutOfRangeException($"Unknown ReadIndexResult: {res.Result}");
		}

		return;

		void PublishLongPoll() {
			_publisher.Publish(
				new SubscriptionMessage.PollStream(
					SubscriptionsService.AllStreamsSubscriptionId,
					res.TfLastCommitPosition, null,
					DateTime.UtcNow + msg.LongPollTimeout.Value, msg
				)
			);
		}
	}

	public async ValueTask HandleAsync(ClientMessage.ReadIndexEventsBackward msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(
					new ClientMessage.ReadIndexEventsBackwardCompleted(
						ReadIndexResult.Expired,
						ResolvedEvent.EmptyArray,
						0,
						false,
						null
					)
				);
			}

			Log.Debug(
				"ReadIndexEventsBackward operation has expired for C:{CommitPosition}/P:{PreparePosition}. Operation expired at {ExpiredAt} after {lifetime:N0} ms.",
				msg.CommitPosition, msg.PreparePosition, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		}

		var res = await _secondaryIndexReaders.ReadBackwards(msg, token);
		switch (res.Result) {
			case ReadIndexResult.Success:
				if (msg.LongPollTimeout.HasValue && res.IsEndOfStream && res.Events.Count is 0) {
					PublishLongPoll();
				} else {
					msg.Envelope.ReplyWith(res);
				}

				break;
			case ReadIndexResult.NotModified:
			case ReadIndexResult.Error:
			case ReadIndexResult.InvalidPosition:
			case ReadIndexResult.IndexNotFound:
				msg.Envelope.ReplyWith(res);
				break;
			default:
				throw new ArgumentOutOfRangeException($"Unknown ReadIndexResult: {res.Result}");
		}

		return;

		void PublishLongPoll() {
			_publisher.Publish(
				new SubscriptionMessage.PollStream(
					SubscriptionsService.AllStreamsSubscriptionId,
					res.TfLastCommitPosition, null,
					DateTime.UtcNow + msg.LongPollTimeout.Value, msg
				)
			);
		}
	}
}
