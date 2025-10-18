// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.Core.Services.Storage;

partial class StorageReaderWorker<TStreamId> : IAsyncHandle<ReadEvent> {
	async ValueTask IAsyncHandle<ReadEvent>.HandleAsync(ReadEvent msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (LogExpiredMessage())
				Log.Debug(
					"Read Event operation has expired for Stream: {stream}, Event Number: {eventNumber}. Operation Expired at {expiryDateTime} after {lifetime:N0} ms.",
					msg.EventStreamId, msg.EventNumber, msg.Expires, msg.Lifetime.TotalMilliseconds);
			return;
		}

		var cts = _multiplexer.Combine([token, msg.CancellationToken]);
		ReadEventCompleted ev;
		try {
			ev = await ReadEvent(msg, cts.Token);
		} catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token) {
			throw new OperationCanceledException(ex.Message, ex, cts.CancellationOrigin);
		} finally {
			await cts.DisposeAsync();
		}

		msg.Envelope.ReplyWith(ev);
	}

	private async ValueTask<ReadEventCompleted> ReadEvent(ReadEvent msg, CancellationToken token) {
		try {
			var streamName = msg.EventStreamId;
			var streamId = _readIndex.GetStreamId(streamName);
			var result = await _readIndex.ReadEvent(streamName, streamId, msg.EventNumber, token);
			var record = result.Result is ReadEventResult.Success && msg.ResolveLinkTos
				? await ResolveLinkToEvent(result.Record, null, token)
				: ResolvedEvent.ForUnresolvedEvent(result.Record);
			if (record is null)
				return NoData(ReadEventResult.AccessDenied);
			if (result.Result is ReadEventResult.NoStream or ReadEventResult.NotFound &&
			    _systemStreams.IsMetaStream(streamId) &&
			    result.OriginalStreamExists.HasValue &&
			    result.OriginalStreamExists.Value) {
				return NoData(ReadEventResult.Success);
			}

			return new(msg.CorrelationId, msg.EventStreamId, result.Result, record.Value, result.Metadata, false, null);
		} catch (Exception exc) when (exc is not OperationCanceledException oce || oce.CancellationToken != token) {
			Log.Error(exc, "Error during processing ReadEvent request.");
			return NoData(ReadEventResult.Error, exc.Message);
		}

		ReadEventCompleted NoData(ReadEventResult result, string error = null)
			=> new(msg.CorrelationId, msg.EventStreamId, result, ResolvedEvent.EmptyEvent, null, false, error);
	}
}
