// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes;

static class ReaderExtensions {
	public static async ValueTask<IReadOnlyList<ResolvedEvent>> ReadRecords(
		this IIndexReader<string> index,
		IEnumerable<IndexQueryRecord> indexRecords,
		CancellationToken cancellationToken
	) {
		using var reader = index.BorrowReader();

		var readEvents = indexRecords.Select(async x => {
			var @event = await reader.ReadEvent<string>(x, cancellationToken);
			return (Record: x, Event: @event);
		});

		var events = (await Task.WhenAll(readEvents))
			.OrderBy(x => x.Record.RowId)
			.Where(x => x.Event is not null)
			.Select(x => x.Event!.Value);

		return events.ToList();
	}

	private static async ValueTask<ResolvedEvent?> ReadEvent<TStreamId>(this TFReaderLease localReader, IndexQueryRecord record, CancellationToken ct) {
		var readPrepare = await localReader.TryReadAt(record.Position.PreparePosition, couldBeScavenged: true, ct);
		if (!readPrepare.Success)
			return null;

		if (readPrepare.LogRecord is not IPrepareLogRecord<TStreamId> prepare)
			throw new($"Incorrect type of log record {readPrepare.LogRecord.RecordType}, expected Prepare record.");

		long eventNumber;
		long commitPosition;

		if (record.Position.PreparePosition != record.Position.CommitPosition) {
			var readCommit = await localReader.TryReadAt(record.Position.CommitPosition, couldBeScavenged: true, ct);
			if (!readCommit.Success)
				return null;

			if (readCommit.LogRecord is not CommitLogRecord commit)
				throw new($"Incorrect type of log record {readCommit.LogRecord.RecordType}, expected Commit record.");

			eventNumber = commit.FirstEventNumber + prepare.TransactionOffset;
			commitPosition = record.Position.CommitPosition;
		} else {
			eventNumber = prepare.ExpectedVersion + 1;
			commitPosition = prepare.LogPosition;
		}

		if (prepare.EventStreamId is not string streamId)
			throw new("Incorrect type of log record event stream id, expected string.");

		if (prepare.EventType is not string eventType)
			throw new("Incorrect type of log record event type, expected string.");

		return ResolvedEvent.ForUnresolvedEvent(
			@event: new(eventNumber, prepare, streamId, eventType),
			commitPosition: commitPosition
		);
	}
}
