// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.Core.Services.Storage;

partial class StorageReaderWorker<TStreamId> :
	IAsyncHandle<ReadIndexEventsForward>,
	IAsyncHandle<ReadIndexEventsBackward> {
	public async ValueTask HandleAsync(ReadIndexEventsForward msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(
					new ReadIndexEventsForwardCompleted(
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
			case ReadIndexResult.Success
				or ReadIndexResult.NotModified
				or ReadIndexResult.Error
				or ReadIndexResult.InvalidPosition
				or ReadIndexResult.IndexNotFound:
				msg.Envelope.ReplyWith(res);
				break;
			default:
				throw new ArgumentOutOfRangeException($"Unknown ReadIndexResult: {res.Result}");
		}
	}

	public async ValueTask HandleAsync(ReadIndexEventsBackward msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(
					new ReadIndexEventsBackwardCompleted(
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
				"ReadIndexEventsBackward operation has expired for C:{CommitPosition}/P:{PreparePosition}. Operation expired at {ExpiredAt} after {lifetime:N0} ms.",
				msg.CommitPosition, msg.PreparePosition, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		}

		var res = await _secondaryIndexReaders.ReadBackwards(msg, token);
		switch (res.Result) {
			case ReadIndexResult.Success
				or ReadIndexResult.NotModified
				or ReadIndexResult.Error
				or ReadIndexResult.InvalidPosition
				or ReadIndexResult.IndexNotFound:
				msg.Envelope.ReplyWith(res);
				break;
			default:
				throw new ArgumentOutOfRangeException($"Unknown ReadIndexResult: {res.Result}");
		}
	}
}
