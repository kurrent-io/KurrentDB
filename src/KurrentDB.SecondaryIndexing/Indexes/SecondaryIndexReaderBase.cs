// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.SecondaryIndexing.Indexes;

public abstract class SecondaryIndexReaderBase(DuckDBConnectionPool db, IReadIndex<string> index) : ISecondaryIndexReader {
	protected DuckDBConnectionPool Db => db;

	protected abstract string GetId(string indexName);

	protected abstract IReadOnlyList<IndexQueryRecord> GetIndexRecordsForwards(string id, TFPos startPosition, int maxCount, bool excludeFirst);

	protected abstract IReadOnlyList<IndexQueryRecord> GetIndexRecordsBackwards(string id, TFPos startPosition, int maxCount, bool excludeFirst);

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
		var resolved = await GetEventsForwards(reader, id, pos, msg.MaxCount, msg.ExcludeStart, token);

		if (resolved.Count == 0) {
			return NoData(ReadIndexResult.Success, true);
		}

		var isEndOfStream = resolved.Count < msg.MaxCount || resolved[^1].Event.LogPosition == lastIndexedPosition;

		return new(ReadIndexResult.Success, resolved, pos, lastIndexedPosition, isEndOfStream, null);

		ReadIndexEventsForwardCompleted NoData(ReadIndexResult result, bool endOfStream, string? error = null)
			=> new(result, ResolvedEvent.EmptyArray, pos, lastIndexedPosition, endOfStream, error);
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
		var resolved = await GetEventsBackwards(reader, id, pos, msg.MaxCount, msg.ExcludeStart, token);

		if (resolved.Count == 0) {
			var response = NoData(ReadIndexResult.Success);
			response.IsEndOfStream = true;
			return response;
		}

		var isEndOfStream = resolved.Count < msg.MaxCount;

		return new(ReadIndexResult.Success, resolved, lastIndexedPosition, isEndOfStream, null);

		ReadIndexEventsBackwardCompleted NoData(ReadIndexResult result, string? error = null)
			=> new(result, ResolvedEvent.EmptyArray, lastIndexedPosition, false, error);
	}

	private async ValueTask<IReadOnlyList<ResolvedEvent>> GetEventsForwards(
		IIndexReader<string> indexReader,
		string id,
		TFPos startPosition,
		int maxCount,
		bool excludeFirst,
		CancellationToken cancellationToken) {
		var indexPrepares = GetIndexRecordsForwards(id, startPosition, maxCount, excludeFirst);
		var events = await indexReader.ReadRecords(indexPrepares, true, cancellationToken);
		return events;
	}

	private async ValueTask<IReadOnlyList<ResolvedEvent>> GetEventsBackwards(
		IIndexReader<string> indexReader,
		string id,
		TFPos startPosition,
		int maxCount,
		bool excludeFirst,
		CancellationToken cancellationToken) {
		var indexPrepares = GetIndexRecordsBackwards(id, startPosition, maxCount, excludeFirst);
		var events = await indexReader.ReadRecords(indexPrepares, false, cancellationToken);
		return events;
	}
}
