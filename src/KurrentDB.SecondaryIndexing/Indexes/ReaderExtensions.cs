// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.LogCommon;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes;

internal static class ReaderExtensions {
	public static async ValueTask<IReadOnlyList<ResolvedEvent>> ReadRecords(
		this IIndexReader<string> index,
		IEnumerable<IndexQueryRecord> indexPrepares,
		bool ascending,
		CancellationToken cancellationToken
	) {
		// ReSharper disable once AccessToDisposedClosure
		var readPrepares = indexPrepares.Select(async x
			=> (Record: x, Prepare: await index.Backend.ReadPrepare(x.LogPosition, cancellationToken)));
		// This way to read is unusual and might cause issues. Observe the impact in the field and revisit.
		var prepared = await Task.WhenAll(readPrepares);
		var recordsQuery = prepared.Where(x => x.Prepare is not null);
		var sorted = ascending
			? recordsQuery.OrderBy(x => x.Record.LogPosition)
			: recordsQuery.OrderByDescending(x => x.Record.LogPosition);
		var records = sorted.Select(x => ResolvedEvent.ForUnresolvedEvent(
			new(x.Record.EventNumber, x.Prepare!, x.Prepare!.EventStreamId, x.Prepare!.EventType),
			x.Record.CommitPosition
		));
		return records.ToList();
	}

	private static async ValueTask<IPrepareLogRecord<TStreamId>?> ReadPrepare<TStreamId>(this IIndexBackend<TStreamId> localReader,
		long logPosition,
		CancellationToken ct)
		=> await localReader.TFReader.TryReadAt(logPosition, couldBeScavenged: true, ct) switch {
			{ Success: false } => null,
			{ LogRecord: IPrepareLogRecord<TStreamId> { RecordType: LogRecordType.Prepare or LogRecordType.Stream or LogRecordType.EventType } r } => r,
			var r => throw new($"Incorrect type of log record {r.LogRecord.RecordType}, expected Prepare record.")
		};
}
