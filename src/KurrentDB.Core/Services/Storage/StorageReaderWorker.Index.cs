// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;

namespace KurrentDB.Core.Services.Storage;

public partial class StorageReaderWorker<TStreamId> : IAsyncHandle<ClientMessage.ReadIndexEventsForward> {
	public async ValueTask HandleAsync(ClientMessage.ReadIndexEventsForward msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(
					new ClientMessage.ReadIndexEventsForwardCompleted(
						msg.CorrelationId,
						ReadIndexResult.Expired,
						null,
						ResolvedEvent.EmptyArray,
						0,
						new(msg.CommitPosition, msg.PreparePosition),
						TFPos.Invalid,
						TFPos.Invalid,
						0,
						false
					)
				);
			}

			Log.Debug(
				"ReadIndexEventsForward operation has expired for C:{CommitPosition}/P:{PreparePosition}. Operation expired at {ExpiredAt} after {lifetime:N0} ms.",
				msg.CommitPosition, msg.PreparePosition, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		}
	}
}
