// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.SecondaryIndexing.Indexes;

public abstract class SecondaryIndexReaderBase(DuckDBConnectionPool sharedPool, IReadIndex<string> index) : ISecondaryIndexReader {
	protected abstract string? GetId(string indexName);

	protected abstract List<IndexQueryRecord> GetDbRecordsForwards(DuckDBConnectionPool pool, string? id, long startPosition, int maxCount, bool excludeFirst);

	protected abstract List<IndexQueryRecord> GetDbRecordsBackwards(DuckDBConnectionPool pool, string? id, long startPosition, int maxCount, bool excludeFirst);

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

		async ValueTask<(long, IReadOnlyList<ResolvedEvent>)> GetEventsForwards(long startPosition) {
			var indexPrepares = GetDbRecordsForwards(
				GetPool(msg.Pool),
				id,
				startPosition,
				msg.MaxCount,
				msg.ExcludeStart);

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

		return new(ReadIndexResult.Success, resolved, pos, lastIndexedPosition, isEndOfStream, null);

		ReadIndexEventsBackwardCompleted NoData(ReadIndexResult result, string? error = null)
			=> new(result, ResolvedEvent.EmptyArray, pos, lastIndexedPosition, false, error);

		async ValueTask<(long, IReadOnlyList<ResolvedEvent>)> GetEventsBackwards(TFPos startPosition) {
			var indexPrepares = GetDbRecordsBackwards(GetPool(msg.Pool),
				id,
				startPosition.PreparePosition,
				msg.MaxCount,
				msg.ExcludeStart);

			var events = await reader.ReadRecords(indexPrepares, false, token);
			return (indexPrepares.Count, events);
		}
	}

	private DuckDBConnectionPool GetPool(DuckDBConnectionPool? pool) {
		return pool ?? sharedPool;
	}
}
