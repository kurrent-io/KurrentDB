// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Text;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.LogCommon;
using KurrentDB.SecondaryIndexing.Indices.DuckDb;
using Serilog;

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
					x.Record.EventNumber,
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
					Encoding.UTF8.GetBytes($"{x.Record.EventNumber}@{x.Prepare!.EventStreamId!.ToString()}"),
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

	public static IEnumerable<ResolvedEvent> ReadAll(this IPublisher publisher, Position startPosition, long maxCount) {
		using var enumerator = GetEnumerator();
		while (enumerator.MoveNext()) {
			if (enumerator.Current is ReadResponse.EventReceived eventReceived)
				yield return eventReceived.Event;
		}

		yield break;

		IEnumerator<ReadResponse> GetEnumerator() {
			return new SyncEnumerator.ReadAllForwardsFiltered(
				bus: publisher,
				position: startPosition,
				maxCount: (ulong)maxCount,
				user: SystemAccounts.System,
				requiresLeader: false,
				deadline: DefaultDeadline,
				maxSearchWindow: null,
				eventFilter: UserEventsFilter.Instance
			);
		}
	}

	public static IEnumerable<ResolvedEvent> ReadStream(this IPublisher publisher, string stream,
		StreamRevision startRevision, long maxCount) {
		using var enumerator = GetEnumerator();

		while (enumerator.MoveNext()) {
			if (enumerator.Current is ReadResponse.EventReceived eventReceived) {
				Log.Debug("Returning event {Event}", eventReceived.Event);
				yield return eventReceived.Event;
			}
		}

		yield break;

		IEnumerator<ReadResponse> GetEnumerator() {
			return new SyncEnumerator.ReadStreamForwardsSync(
				bus: publisher,
				streamName: stream,
				startRevision: startRevision,
				maxCount: (ulong)maxCount,
				resolveLinks: true,
				user: SystemAccounts.System,
				requiresLeader: false,
				deadline: DefaultDeadline
			);
		}
	}

	public static IEnumerable<ResolvedEvent> ReadEvents(this IPublisher publisher, long[] logPositions) {
		using var enumerator = GetEnumerator();

		while (enumerator.MoveNext()) {
			if (enumerator.Current is ReadResponse.EventReceived eventReceived)
				yield return eventReceived.Event;
		}

		yield break;

		IEnumerator<ReadResponse> GetEnumerator() {
			return new Enumerator.ReadLogEventsSync(
				bus: publisher,
				logPositions: logPositions,
				user: SystemAccounts.System,
				deadline: DefaultDeadline
			);
		}
	}

	private static readonly DateTime DefaultDeadline = DateTime.UtcNow.AddYears(1);

	private class UserEventsFilter : IEventFilter {
		public static readonly UserEventsFilter Instance = new();

		public bool IsEventAllowed(EventRecord eventRecord) {
			return !eventRecord.EventType.StartsWith('$') && !eventRecord.EventStreamId.StartsWith('$');
		}
	}
}
