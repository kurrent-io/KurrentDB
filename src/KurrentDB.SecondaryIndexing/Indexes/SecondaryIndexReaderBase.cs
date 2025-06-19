// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Readers;
using static KurrentDB.Core.Services.Storage.StorageReaderWorker<string>;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;

namespace KurrentDB.SecondaryIndexing.Indexes;

public interface ISecondaryIndexReader: IVirtualStreamReader {

}

public record struct IndexedPrepare(long Version, long LogPosition);

internal abstract class SecondaryIndexReaderBase(IReadIndex<string> index) : ISecondaryIndexReader {
	protected abstract long GetId(string streamName);

	protected abstract long GetLastIndexedSequence(long id);

	protected abstract IEnumerable<IndexedPrepare> GetIndexRecords(long id, long fromEventNumber, long toEventNumber);

	public ValueTask<ClientMessage.ReadStreamEventsForwardCompleted> ReadForwards(
		ClientMessage.ReadStreamEventsForward msg,
		CancellationToken token
	) =>
		ReadForwards(msg, index.IndexReader, index.LastIndexedPosition, token);

	public ValueTask<ClientMessage.ReadStreamEventsBackwardCompleted> ReadBackwards(
		ClientMessage.ReadStreamEventsBackward msg,
		CancellationToken token
	) =>
		ReadBackwards(msg, index.IndexReader, index.LastIndexedPosition, token);

	private async ValueTask<IReadOnlyList<ResolvedEvent>> GetEvents(
		IIndexReader<string> indexReader,
		string virtualStreamId,
		long id,
		long fromEventNumber,
		long toEventNumber,
		CancellationToken cancellationToken) {
		var indexPrepares = GetIndexRecords(id, fromEventNumber, toEventNumber);
		return await indexReader.ReadRecords(virtualStreamId, indexPrepares, cancellationToken);
	}

	public long GetLastEventNumber(string streamName) {
		var id = GetId(streamName);
		return id >= 0 ? GetLastIndexedSequence(id) : id;
	}

	public abstract long GetLastIndexedPosition(string streamId);

	public abstract bool CanReadStream(string streamId);

	private async ValueTask<ClientMessage.ReadStreamEventsBackwardCompleted> ReadBackwards(
		ClientMessage.ReadStreamEventsBackward msg, IIndexReader<string> reader, long lastIndexedPosition,
		CancellationToken token
	) {
		var id = GetId(msg.EventStreamId);
		var lastEventNumber = GetLastIndexedSequence(id);

		if (msg.ValidationStreamVersion.HasValue && lastEventNumber == msg.ValidationStreamVersion)
			return NoData(msg, ReadStreamResult.NotModified, lastIndexedPosition,
				msg.ValidationStreamVersion.Value);
		if (lastEventNumber == 0)
			return NoData(msg, ReadStreamResult.NoStream, lastIndexedPosition,
				msg.ValidationStreamVersion ?? 0);

		long endEventNumber = msg.FromEventNumber < 0 ? lastEventNumber : msg.FromEventNumber;
		long startEventNumber = Math.Max(0L, endEventNumber - msg.MaxCount + 1);
		var resolved = await GetEvents(reader, msg.EventStreamId, id, startEventNumber, endEventNumber, token);

		if (resolved.Count == 0)
			return NoData(msg, ReadStreamResult.Success, lastIndexedPosition,
				msg.ValidationStreamVersion ?? 0);

		var records = resolved.OrderByDescending(x => x.OriginalEvent.EventNumber).ToArray();
		var isEndOfStream = startEventNumber == 0 || (startEventNumber <= lastEventNumber &&
		                                              (records.Length is 0 || records[^1].OriginalEventNumber !=
			                                              startEventNumber));
		long nextEventNumber = isEndOfStream ? -1 : Math.Min(startEventNumber - 1, lastEventNumber);

		return new(msg.CorrelationId, msg.EventStreamId, endEventNumber, msg.MaxCount,
			ReadStreamResult.Success, records, StreamMetadata.Empty, false, string.Empty,
			nextEventNumber, lastEventNumber, isEndOfStream, lastIndexedPosition);
	}

	private async ValueTask<ClientMessage.ReadStreamEventsForwardCompleted> ReadForwards(
		ClientMessage.ReadStreamEventsForward msg,
		IIndexReader<string> reader,
		long lastIndexedPosition,
		CancellationToken token
	) {
		var id = GetId(msg.EventStreamId);
		var lastEventNumber = GetLastIndexedSequence(id);

		if (msg.ValidationStreamVersion.HasValue && lastEventNumber == msg.ValidationStreamVersion)
			return NoData(msg, ReadStreamResult.NotModified, lastIndexedPosition,
				msg.ValidationStreamVersion.Value);

		var fromEventNumber = msg.FromEventNumber < 0 ? 0 : msg.FromEventNumber;
		var maxCount = msg.MaxCount;
		var endEventNumber = fromEventNumber > long.MaxValue - maxCount + 1
			? long.MaxValue
			: fromEventNumber + maxCount - 1;
		var resolved = await GetEvents(reader, msg.EventStreamId, id, fromEventNumber, endEventNumber, token);

		if (resolved.Count == 0)
			return NoData(msg, ReadStreamResult.Success, lastIndexedPosition,
				msg.ValidationStreamVersion ?? 0);

		long nextEventNumber = Math.Min(endEventNumber + 1, lastEventNumber + 1);
		if (resolved.Count > 0)
			nextEventNumber = resolved[^1].OriginalEventNumber + 1;
		var isEndOfStream = endEventNumber >= lastEventNumber;

		return new(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount,
			ReadStreamResult.Success, resolved, StreamMetadata.Empty, false, string.Empty,
			nextEventNumber, lastEventNumber, isEndOfStream, lastIndexedPosition);
	}
}
