// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Readers;
using KurrentDB.SecondaryIndexing.Storage;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.SecondaryIndexing.Indexes;

public abstract class SecondaryIndexReaderBase(IReadIndex<string> index) : ISecondaryIndexReader {
	protected abstract int GetId(string streamName);

	protected abstract IEnumerable<IndexQueryRecord> GetIndexRecords(int id, TFPos startPosition, int maxCount);

	public ValueTask<ReadIndexEventsForwardCompleted> ReadForwards(ReadIndexEventsForward msg, CancellationToken token) =>
		ReadForwards(msg, index.IndexReader, index.LastIndexedPosition, token);

	// public ValueTask<ReadStreamEventsBackwardCompleted> ReadBackwards(ReadStreamEventsBackward msg, CancellationToken token) =>
		// ReadBackwards(msg, index.IndexReader, index.LastIndexedPosition, token);

	async ValueTask<IReadOnlyList<ResolvedEvent>> GetEvents(
		IIndexReader<string> indexReader,
		int id,
		TFPos startPosition,
		int maxCount,
		CancellationToken cancellationToken) {
		var indexPrepares = GetIndexRecords(id, startPosition, maxCount);
		return await indexReader.ReadRecords(indexPrepares, cancellationToken);
	}

	public abstract long GetLastIndexedPosition(string indexName);

	public abstract bool CanReadIndex(string indexName);

	async ValueTask<ReadIndexEventsForwardCompleted> ReadForwards(
		ReadIndexEventsForward msg,
		IIndexReader<string> reader,
		long lastIndexedPosition,
		CancellationToken token
	) {
		var pos = new TFPos(msg.CommitPosition, msg.PreparePosition);
		if (pos.CommitPosition < 0 || pos.PreparePosition < 0)
			return NoData(ReadIndexResult.InvalidPosition, "Invalid position.");
		if (msg.ValidationTfLastCommitPosition == lastIndexedPosition)
			return NoData(ReadIndexResult.NotModified);

		var id = GetId(msg.IndexName);
		var resolved = await GetEvents(reader, id, pos, msg.MaxCount, token);

		if (resolved.Count == 0)
			return NoData(ReadIndexResult.Success);

		var isEndOfStream = resolved[^1].EventPosition!.Value.PreparePosition >= lastIndexedPosition;

		return new(msg.CorrelationId, ReadIndexResult.Success, null, resolved, msg.MaxCount, pos, TFPos.Invalid, TFPos.Invalid, lastIndexedPosition, isEndOfStream);

		ReadIndexEventsForwardCompleted NoData(ReadIndexResult result, string? error = null)
			=> new(msg.CorrelationId, result, error, ResolvedEvent.EmptyArray,
				msg.MaxCount, pos, TFPos.Invalid, TFPos.Invalid, lastIndexedPosition, false);
	}

	// private async ValueTask<ReadStreamEventsBackwardCompleted> ReadBackwards(
	// 	ReadStreamEventsBackward msg,
	// 	IIndexReader<string> reader,
	// 	long lastIndexedPosition,
	// 	CancellationToken token
	// ) {
	// 	var id = GetId(msg.EventStreamId);
	// 	var lastEventNumber = GetLastIndexedSequence(id);
	//
	// 	if (msg.ValidationStreamVersion.HasValue && lastEventNumber == msg.ValidationStreamVersion)
	// 		return NoData(msg, ReadStreamResult.NotModified, lastIndexedPosition,
	// 			msg.ValidationStreamVersion.Value);
	// 	if (lastEventNumber == -1)
	// 		return NoData(msg, ReadStreamResult.NoStream, lastIndexedPosition,
	// 			msg.ValidationStreamVersion ?? -1);
	//
	// 	long endEventNumber = msg.FromEventNumber < 0 || msg.FromEventNumber > lastEventNumber
	// 		? lastEventNumber
	// 		: msg.FromEventNumber;
	// 	long startEventNumber = Math.Max(0L, endEventNumber - msg.MaxCount + 1);
	// 	var resolved = await GetEvents(reader, msg.EventStreamId, id, startEventNumber, endEventNumber, token);
	//
	// 	var records = resolved.OrderByDescending(x => x.OriginalEvent.EventNumber).ToArray();
	// 	var isEndOfStream = startEventNumber == 0 || (startEventNumber <= lastEventNumber &&
	// 	                                              (records.Length is 0 || records[^1].OriginalEventNumber !=
	// 		                                              startEventNumber));
	// 	long nextEventNumber = isEndOfStream ? -1 : Math.Min(startEventNumber - 1, lastEventNumber);
	//
	// 	if (resolved.Count == 0)
	// 		return NoData(msg, ReadStreamResult.Success, lastIndexedPosition,
	// 			msg.ValidationStreamVersion ?? lastEventNumber, nextEventNumber);
	//
	// 	return new(msg.CorrelationId, msg.EventStreamId, endEventNumber, msg.MaxCount,
	// 		ReadStreamResult.Success, records, StreamMetadata.Empty, false, string.Empty,
	// 		nextEventNumber, lastEventNumber, isEndOfStream, lastIndexedPosition);
	// }
}
