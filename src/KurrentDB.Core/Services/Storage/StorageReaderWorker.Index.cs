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
		ReadIndexEventsForwardCompleted res;
		var cts = _multiplexer.Combine(msg.Lifetime, [token, msg.CancellationToken]);
		var leaseTaken = false;
		try {
			await AcquireRateLimitLeaseAsync(cts.Token);
			leaseTaken = true;

			res = await _secondaryIndexReaders.ReadForwards(msg, cts.Token);
		} catch (OperationCanceledException e) when (e.CancellationToken == cts.Token) {
			if (!cts.IsTimedOut)
				throw new OperationCanceledException(null, e, cts.CancellationOrigin);

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
		} finally {
			await cts.DisposeAsync();

			if (leaseTaken)
				ReleaseRateLimitLease();
		}

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
		ReadIndexEventsBackwardCompleted res;
		var cts = _multiplexer.Combine(msg.Lifetime, [token, msg.CancellationToken]);
		var leaseTaken = false;
		try {
			await AcquireRateLimitLeaseAsync(cts.Token);
			leaseTaken = true;

			res = await _secondaryIndexReaders.ReadBackwards(msg, cts.Token);
		} catch (OperationCanceledException e) when (e.CancellationToken == cts.Token) {
			if (!cts.IsTimedOut)
				throw new OperationCanceledException(null, e, cts.CancellationOrigin);

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
		} finally {
			await cts.DisposeAsync();

			if (leaseTaken)
				ReleaseRateLimitLease();
		}

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
