// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.Core.Services.Storage;

partial class StorageReaderWorker<TStreamId> : IAsyncHandle<ReadEvent> {
	async ValueTask IAsyncHandle<ReadEvent>.HandleAsync(ReadEvent msg, CancellationToken token) {
		ReadEventCompleted completed;
		var cts = _multiplexer.Combine(msg.Lifetime, [token, msg.CancellationToken]);
		try {
			var streamName = msg.EventStreamId;
			var streamId = _readIndex.GetStreamId(streamName);
			var result = await _readIndex.ReadEvent(streamName, streamId, msg.EventNumber, cts.Token);

			completed = result switch {
				{ Result: ReadEventResult.Success } when msg.ResolveLinkTos =>
					(await ResolveLinkToEvent(result.Record, null, cts.Token)).TryGetValue(out var record)
						? HasData(result, record)
						: NoData(ReadEventResult.AccessDenied),
				{ Result: ReadEventResult.NoStream or ReadEventResult.NotFound, OriginalStreamExists: true }
					when _systemStreams.IsMetaStream(streamId) => NoData(ReadEventResult.Success),
				_ => HasData(result, ResolvedEvent.ForUnresolvedEvent(result.Record))
			};
		} catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token) {
			if (!cts.IsTimedOut)
				throw new OperationCanceledException(ex.Message, ex, cts.CancellationOrigin);

			if (LogExpiredMessage())
				Log.Debug(
					"Read Event operation has expired for Stream: {stream}, Event Number: {eventNumber}. Operation Expired at {expiryDateTime} after {lifetime:N0} ms.",
					msg.EventStreamId, msg.EventNumber, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return; // nothing to reply
		} catch (Exception exc) {
			Log.Error(exc, "Error during processing ReadEvent request.");
			completed = NoData(ReadEventResult.Error, exc.Message);
		} finally {
			await cts.DisposeAsync();
		}

		msg.Envelope.ReplyWith(completed);

		ReadEventCompleted HasData(in IndexReadEventResult result, ResolvedEvent record)
			=> new(msg.CorrelationId, msg.EventStreamId, result.Result, record, result.Metadata, false, null);

		ReadEventCompleted NoData(ReadEventResult result, string error = null)
			=> new(msg.CorrelationId, msg.EventStreamId, result, ResolvedEvent.EmptyEvent, null, false, error);
	}
}
