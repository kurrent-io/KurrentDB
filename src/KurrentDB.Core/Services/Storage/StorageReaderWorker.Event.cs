// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.Core.Services.Storage;

partial class StorageReaderWorker<TStreamId> : IAsyncHandle<ReadEvent> {
	async ValueTask IAsyncHandle<ReadEvent>.HandleAsync(ReadEvent msg, CancellationToken token) {
		if (msg.Expires < DateTime.UtcNow) {
			if (LogExpiredMessage())
				Log.Debug(
					"Read Event operation has expired for Stream: {stream}, Event Number: {eventNumber}. Operation Expired at {expiryDateTime} after {lifetime:N0} ms.",
					msg.EventStreamId, msg.EventNumber, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		}

		msg.Envelope.ReplyWith(await ReadEvent(msg, token));
	}

	private async ValueTask<ReadEventCompleted> ReadEvent(ReadEvent msg, CancellationToken token) {
		var cts = _multiplexer.Combine([token, msg.CancellationToken]);
		try {
			var streamName = msg.EventStreamId;
			var streamId = _readIndex.GetStreamId(streamName);
			var result = await _readIndex.ReadEvent(streamName, streamId, msg.EventNumber, cts.Token);

			ResolvedEvent record;
			switch (result) {
				case { Result: ReadEventResult.Success } when msg.ResolveLinkTos:
					if ((await ResolveLinkToEvent(result.Record, null, cts.Token)).TryGetValue(out record))
						break;

					return NoData(ReadEventResult.AccessDenied);
				case { Result: ReadEventResult.NoStream or ReadEventResult.NotFound, OriginalStreamExists: true }
					when _systemStreams.IsMetaStream(streamId):
					return NoData(ReadEventResult.Success);
				default:
					record = ResolvedEvent.ForUnresolvedEvent(result.Record);
					break;
			}

			return new(msg.CorrelationId, msg.EventStreamId, result.Result, record, result.Metadata, false, null);
		} catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token) {
			throw new OperationCanceledException(ex.Message, ex, cts.CancellationOrigin);
		} catch (Exception exc) {
			Log.Error(exc, "Error during processing ReadEvent request.");
			return NoData(ReadEventResult.Error, exc.Message);
		} finally {
			await cts.DisposeAsync();
		}

		ReadEventCompleted NoData(ReadEventResult result, string error = null)
			=> new(msg.CorrelationId, msg.EventStreamId, result, ResolvedEvent.EmptyEvent, null, false, error);
	}
}
