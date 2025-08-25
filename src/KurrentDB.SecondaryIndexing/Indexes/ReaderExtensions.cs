// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.LogCommon;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes;

static class ReaderExtensions {
	public static async ValueTask<IReadOnlyList<ResolvedEvent>> ReadRecords(
		this IIndexReader<string> index,
		IEnumerable<IndexQueryRecord> indexPrepares,
		bool ascending,
		CancellationToken cancellationToken
	) {
		using var reader = index.BorrowReader();
		// ReSharper disable once AccessToDisposedClosure
		var readPrepares = indexPrepares.Select(async x => (Record: x, Prepare: await reader.ReadPrepare<string>(x.LogPosition, cancellationToken)));
		var prepared = await Task.WhenAll(readPrepares);
		var recordsQuery = prepared.Where(x => x.Prepare != null);
		var sorted = ascending ? recordsQuery.OrderBy(x => x.Record.RowId) : recordsQuery.OrderByDescending(x => x.Record.RowId);
		var records = sorted.Select(x => ResolvedEvent.ForUnresolvedEvent(
			new(x.Prepare!.ExpectedVersion + 1, x.Prepare, x.Prepare!.EventStreamId, x.Prepare!.EventType)
		));
		return records.ToList();
	}

	static async ValueTask<IPrepareLogRecord<TStreamId>?> ReadPrepare<TStreamId>(this TFReaderLease localReader,
		long logPosition, CancellationToken ct) {
		var r = await localReader.TryReadAt(logPosition, couldBeScavenged: true, ct);
		if (!r.Success)
			return null;

		if (r.LogRecord.RecordType is not LogRecordType.Prepare
		    and not LogRecordType.Stream
		    and not LogRecordType.EventType)
			throw new($"Incorrect type of log record {r.LogRecord.RecordType}, expected Prepare record.");
		return (IPrepareLogRecord<TStreamId>)r.LogRecord;
	}
}
