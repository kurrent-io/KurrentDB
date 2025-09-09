// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;
using Microsoft.Extensions.Logging;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.SecondaryIndexing.Indexes;

public abstract class SecondaryIndexReaderBase(DuckDBConnectionPool db, IReadIndex<string> index, ILogger log) : ISecondaryIndexReader {
	protected DuckDBConnectionPool Db => db;
	protected ILogger Log = log;

	protected abstract string GetId(string indexName);

	protected abstract IEnumerable<IndexQueryRecord> GetInflightForwards(string id, long startPosition, int maxCount, bool excludeFirst);

	protected abstract List<IndexQueryRecord> GetDbRecordsForwards(string id, long startPosition, int maxCount, bool excludeFirst);

	protected abstract IEnumerable<IndexQueryRecord> GetInflightBackwards(string id, long startPosition, int maxCount, bool excludeFirst);

	protected abstract List<IndexQueryRecord> GetDbRecordsBackwards(string id, long startPosition, int maxCount, bool excludeFirst);

	public ValueTask<ReadIndexEventsForwardCompleted> ReadForwards(ReadIndexEventsForward msg, CancellationToken token)
		=> ReadForwards(msg, index.IndexReader, index.LastIndexedPosition, token);

	public ValueTask<ReadIndexEventsBackwardCompleted> ReadBackwards(ReadIndexEventsBackward msg, CancellationToken token)
		=> ReadBackwards(msg, index.IndexReader, index.LastIndexedPosition, token);

	public abstract TFPos GetLastIndexedPosition(string indexName);

	public abstract bool CanReadIndex(string indexName);

	private async ValueTask<ReadIndexEventsForwardCompleted> ReadForwards(
		ReadIndexEventsForward msg,
		IIndexReader<string> reader,
		long lastIndexedPosition,
		CancellationToken token
	) {
		var pos = new TFPos(msg.CommitPosition, msg.PreparePosition);
		if (pos.CommitPosition < 0 || pos.PreparePosition < 0) {
			return NoData(ReadIndexResult.InvalidPosition, false, "Invalid position.");
		}

		if (msg.ValidationTfLastCommitPosition == lastIndexedPosition) {
			return NoData(ReadIndexResult.NotModified, true);
		}

		var id = GetId(msg.IndexName);
		var (indexRecordsCount, resolved) = await GetEventsForwards(msg.PreparePosition);

		if (resolved.Count == 0) {
			return NoData(ReadIndexResult.Success, true);
		}

		var isEndOfStream = indexRecordsCount < msg.MaxCount || resolved[^1].Event.LogPosition == lastIndexedPosition;

		return new(ReadIndexResult.Success, resolved, pos, lastIndexedPosition, isEndOfStream, null);

		ReadIndexEventsForwardCompleted NoData(ReadIndexResult result, bool endOfStream, string? error = null)
			=> new(result, ResolvedEvent.EmptyArray, pos, lastIndexedPosition, endOfStream, error);

		IReadOnlyList<IndexQueryRecord> GetIndexRecordsForwards(long startPosition) {
			var maxCount = msg.MaxCount;
			var inFlight = GetInflightForwards(id, startPosition, maxCount, msg.ExcludeStart).ToArray();
			if (inFlight.Length == maxCount) {
				return inFlight;
			}

			var count = inFlight.Length > 0 ? maxCount - inFlight.Length : maxCount;
			var range = GetDbRecordsForwards(id, startPosition, count, msg.ExcludeStart);
			if (range.Count == 0) {
				return inFlight;
			}

			if (inFlight.Length > 0) {
				var last = range[^1].RowId + 1;
				range.AddRange(inFlight.Select((record, i) => record with { RowId = last + i }));
			}

			return range;
		}

		async ValueTask<(long, IReadOnlyList<ResolvedEvent>)> GetEventsForwards(long startPosition) {
			var indexPrepares = GetIndexRecordsForwards(startPosition);
			var events = await reader.ReadRecords(indexPrepares, true, token);
			return (indexPrepares.Count, events);
		}
	}

	private async ValueTask<ReadIndexEventsBackwardCompleted> ReadBackwards(
		ReadIndexEventsBackward msg,
		IIndexReader<string> reader,
		long lastIndexedPosition,
		CancellationToken token
	) {
		var pos = new TFPos(msg.CommitPosition, msg.PreparePosition);
		if (pos.CommitPosition < 0 || pos.PreparePosition < 0) {
			pos = new(long.MaxValue, long.MaxValue);
		}

		if (msg.ValidationTfLastCommitPosition == lastIndexedPosition) {
			return NoData(ReadIndexResult.NotModified);
		}

		var id = GetId(msg.IndexName);
		var (indexRecordsCount, resolved) = await GetEventsBackwards(pos);

		if (resolved.Count == 0) {
			var response = NoData(ReadIndexResult.Success);
			response.IsEndOfStream = true;
			return response;
		}

		var isEndOfStream = indexRecordsCount < msg.MaxCount;

		return new(ReadIndexResult.Success, resolved, lastIndexedPosition, isEndOfStream, null);

		ReadIndexEventsBackwardCompleted NoData(ReadIndexResult result, string? error = null)
			=> new(result, ResolvedEvent.EmptyArray, lastIndexedPosition, false, error);

		IReadOnlyList<IndexQueryRecord> GetIndexRecordsBackwards(TFPos startPosition) {
			var maxCount = msg.MaxCount;
			var inFlight = GetInflightBackwards(id, startPosition.PreparePosition, maxCount, msg.ExcludeStart).ToArray();
			if (inFlight.Length == maxCount) {
				return inFlight;
			}

			int count;
			long start;
			bool excl;
			if (inFlight.Length > 0) {
				count = maxCount - inFlight.Length;
				start = inFlight[0].LogPosition;
				excl = true;
			} else {
				count = maxCount;
				start = startPosition.PreparePosition;
				excl = msg.ExcludeStart;
			}
			var range = GetDbRecordsBackwards(id, start, count, excl);
			if (range.Count == 0) {
				return inFlight;
			}

			if (inFlight.Length > 0) {
				var last = range[^1].RowId + 1;
				range.AddRange(inFlight.Select((record, i) => record with { RowId = last + i }));
			}

			return range;
		}

		async ValueTask<(long, IReadOnlyList<ResolvedEvent>)> GetEventsBackwards(TFPos startPosition) {
			var indexPrepares = GetIndexRecordsBackwards(startPosition);
			var events = await reader.ReadRecords(indexPrepares, false, token);
			return (indexPrepares.Count, events);
		}
	}
}
