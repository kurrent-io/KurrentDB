// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.LogCommon;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.Core.Services.Storage;

partial class StorageReaderWorker<TStreamId> : IAsyncHandle<ReadLogEvents> {
	async ValueTask IAsyncHandle<ReadLogEvents>.HandleAsync(ReadLogEvents msg, CancellationToken token) {
		if (msg.CancellationToken.IsCancellationRequested)
			return;

		if (msg.Expires < DateTime.UtcNow) {
			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new ReadLogEventsCompleted(msg.CorrelationId, ReadEventResult.Expired, [], null));
			}

			if (LogExpiredMessage())
				Log.Debug("Read Log Events operation has expired. Operation Expired at {expiryDateTime}", msg.Expires);
			return;
		}

		ReadLogEventsCompleted ev;
		var cts = _multiplexer.Combine([token, msg.CancellationToken]);
		try {
			ev = await ReadLogEvents(msg, cts.Token);
		} catch (OperationCanceledException e) when (e.CancellationToken == cts.Token) {
			throw new OperationCanceledException(e.Message, e, cts.CancellationOrigin);
		} finally {
			await cts.DisposeAsync();
		}

		msg.Envelope.ReplyWith(ev);
	}

	private async ValueTask<ReadLogEventsCompleted> ReadLogEvents(ReadLogEvents msg, CancellationToken token) {
		try {
			var reader = _readIndex.IndexReader;
			var readPrepares = msg.LogPositions.Select(async (pos, index) => (Index: index, Prepare: await reader.ReadPrepare<TStreamId>(pos, token)));
			// This way to read is unusual and might cause issues. Observe the impact in the field and revisit.
			var prepared = (await Task.WhenAll(readPrepares))
				.Select(x => ResolvedEvent.ForUnresolvedEvent(new(x.Index, x.Prepare, x.Prepare!.EventStreamId!.ToString()!, x.Prepare.EventType.ToString())));
			return new(msg.CorrelationId, ReadEventResult.Success, prepared.ToArray(), null);
		} catch (Exception e) {
			Log.Error(e, "Error during processing ReadEvent request.");
			return NoData(msg, ReadEventResult.Error, e.Message);
		}

		static ReadLogEventsCompleted NoData(ReadLogEvents msg, ReadEventResult result, string error = null) {
			return new(msg.CorrelationId, result, [], error);
		}
	}
}

file static class ReaderExtensions {
	internal static async ValueTask<IPrepareLogRecord<TStreamId>> ReadPrepare<TStreamId>(this IIndexReader<TStreamId> localReader, long logPosition, CancellationToken ct) {
		var r = await localReader.Backend.TFReader.TryReadAt(logPosition, couldBeScavenged: true, ct);
		if (!r.Success)
			return null;

		if (r.LogRecord.RecordType is not LogRecordType.Prepare
			and not LogRecordType.Stream
			and not LogRecordType.EventType)
			throw new($"Incorrect type of log record {r.LogRecord.RecordType}, expected Prepare record.");
		return (IPrepareLogRecord<TStreamId>)r.LogRecord;
	}
}
