// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.LogCommon;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.Core.Services.Storage;

partial class StorageReaderWorker<TStreamId> : IAsyncHandle<ReadLogEvents> {
	async ValueTask IAsyncHandle<ReadLogEvents>.HandleAsync(ReadLogEvents msg, CancellationToken token) {
		if (msg.Expires < DateTime.UtcNow) {
			if (msg.ReplyOnExpired) {
				msg.Envelope.ReplyWith(new ReadLogEventsCompleted(msg.CorrelationId, ReadEventResult.Expired, [], null));
			}

			if (LogExpiredMessage())
				Log.Debug("Read Log Events operation has expired. Operation Expired at {expiryDateTime}", msg.Expires);
			return;
		}

		msg.Envelope.ReplyWith(await ReadLogEvents(msg, token));
	}

	private async ValueTask<ReadLogEventsCompleted> ReadLogEvents(ReadLogEvents msg, CancellationToken token) {
		var cts = _multiplexer.Combine([token, msg.CancellationToken]);
		try {
			var reader = _readIndex.IndexReader;
			var readPrepares =
				msg.LogPositions.Select(async (pos, index) => (Index: index, Prepare: await reader.Backend.ReadPrepare(pos, token)));
			// This way to read is unusual and might cause issues. Observe the impact in the field and revisit.
			var prepared = (await Task.WhenAll(readPrepares))
				.Select(x => ResolvedEvent.ForUnresolvedEvent(new(x.Index, x.Prepare, x.Prepare!.EventStreamId!.ToString()!,
					x.Prepare.EventType.ToString())));
			return new(msg.CorrelationId, ReadEventResult.Success, prepared.ToArray(), null);
		} catch (OperationCanceledException e) when (e.CancellationToken == cts.Token) {
			throw new OperationCanceledException(e.Message, e, cts.CancellationOrigin);
		} catch (Exception e) {
			Log.Error(e, "Error during processing ReadEvent request.");
			return NoData(msg, ReadEventResult.Error, e.Message);
		} finally {
			await cts.DisposeAsync();
		}

		static ReadLogEventsCompleted NoData(ReadLogEvents msg, ReadEventResult result, string error = null) {
			return new(msg.CorrelationId, result, [], error);
		}
	}
}

file static class ReaderExtensions {
	internal static async ValueTask<IPrepareLogRecord<TStreamId>> ReadPrepare<TStreamId>(this IIndexBackend<TStreamId> localReader,
		long logPosition,
		CancellationToken ct)
		=> await localReader.TFReader.TryReadAt(logPosition, couldBeScavenged: true, ct) switch {
			{ Success: false } => null,
			{ LogRecord: IPrepareLogRecord<TStreamId> { RecordType: LogRecordType.Prepare or LogRecordType.Stream or LogRecordType.EventType } r } => r,
			var r => throw new($"Incorrect type of log record {r.LogRecord.RecordType}, expected Prepare record.")
		};
}
