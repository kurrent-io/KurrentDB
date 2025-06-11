// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Text;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.LogCommon;
using KurrentDB.SecondaryIndexing.Indexes;

namespace KurrentDB.SecondaryIndexing.Readers;

public static class ReaderExtensions {
	public static async ValueTask<IReadOnlyList<ResolvedEvent>> ReadRecords<TStreamId>(
		this IIndexReader<TStreamId> index,
		string virtualStreamId,
		IEnumerable<IndexedPrepare> indexPrepares,
		CancellationToken cancellationToken
	) {
		using var reader = index.BorrowReader();
		// ReSharper disable once AccessToDisposedClosure
		var readPrepares = indexPrepares.Select(async x =>
			(Record: x, Prepare: await reader.ReadPrepare<TStreamId>(x.LogPosition, cancellationToken)));
		var prepared = await Task.WhenAll(readPrepares);
		var recordsQuery = prepared.Where(x => x.Prepare != null).OrderBy(x => x.Record.Version).ToList();
		var records = recordsQuery
			.Select(x => ResolvedEvent.ForResolvedLink(
				new EventRecord(
					x.Prepare!.ExpectedVersion,
					x.Prepare,
					x.Prepare!.EventStreamId!.ToString(),
					x.Prepare!.EventType!.ToString()
				),
				new EventRecord(
					x.Record.Version,
					x.Prepare!.LogPosition,
					x.Prepare!.CorrelationId,
					x.Prepare!.EventId,
					x.Prepare!.TransactionPosition,
					x.Prepare!.TransactionOffset,
					virtualStreamId,
					x.Record.Version,
					x.Prepare!.TimeStamp,
					x.Prepare!.Flags,
					"$>",
					Encoding.UTF8.GetBytes($"{x.Prepare!.ExpectedVersion}@{x.Prepare!.EventStreamId!.ToString()}"),
					[],
					[]
				))
			);
		var result = records.ToList();
		return result;
	}

	private static async ValueTask<IPrepareLogRecord<TStreamId>?> ReadPrepare<TStreamId>(this TFReaderLease localReader,
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
