// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Duck.Infrastructure;
using EventStore.Core.Services.Storage;
using EventStore.Core.Services.Storage.InMemory;
using EventStore.Core.Services.Storage.ReaderIndex;
using static EventStore.Core.Messages.ClientMessage;
using ReadStreamResult = EventStore.Core.Data.ReadStreamResult;

namespace EventStore.Core.Duck;

public record struct IndexedPrepare(long Version, int EventNumber, long LogPosition);

abstract class DuckIndexReader(IReadIndex<string> index) : IVirtualStreamReader {
	protected abstract long GetId(string streamName);

	protected abstract long GetLastNumber(long id);

	protected abstract IEnumerable<IndexedPrepare> GetIndexRecords(long id, long fromEventNumber, long toEventNumber);

	public async ValueTask<ReadStreamEventsForwardCompleted> ReadForwards(ReadStreamEventsForward msg, CancellationToken token) {
		return await ReadForwards(msg, index.IndexReader, index.LastIndexedPosition, token);
	}

	public async ValueTask<ReadStreamEventsBackwardCompleted> ReadBackwards(ReadStreamEventsBackward msg, CancellationToken token) {
		return await ReadBackwards(msg, index.IndexReader, index.LastIndexedPosition, token);
	}

	async ValueTask<IReadOnlyList<ResolvedEvent>> GetEvents(IIndexReader<string> indexReader, string virtualStreamId, long id, long fromEventNumber, long toEventNumber,
		CancellationToken cancellationToken) {
		var indexPrepares = GetIndexRecords(id, fromEventNumber, toEventNumber);
		return await indexReader.ReadRecords(virtualStreamId, indexPrepares, cancellationToken);
	}

	public ValueTask<long> GetLastEventNumber(string streamName) {
		var id = GetId(streamName);
		return ValueTask.FromResult(id >= 0 ? GetLastNumber(id) : id);
	}

	public abstract ValueTask<long> GetLastIndexedPosition();

	public abstract bool OwnStream(string streamId);

	async Task<ReadStreamEventsBackwardCompleted> ReadBackwards(
		ReadStreamEventsBackward msg, IIndexReader<string> reader, long lastIndexedPosition, CancellationToken token
	) {
		var id = GetId(msg.EventStreamId);
		var lastEventNumber = GetLastNumber(id);

		if (msg.ValidationStreamVersion.HasValue && lastEventNumber == msg.ValidationStreamVersion)
			return StorageReaderWorker<string>.NoData(msg, ReadStreamResult.NotModified, lastIndexedPosition, msg.ValidationStreamVersion.Value);
		if (lastEventNumber == 0)
			return StorageReaderWorker<string>.NoData(msg, ReadStreamResult.NoStream, lastIndexedPosition, msg.ValidationStreamVersion ?? 0);

		long endEventNumber = msg.FromEventNumber < 0 ? lastEventNumber : msg.FromEventNumber;
		long startEventNumber = Math.Max(0L, endEventNumber - msg.MaxCount + 1);
		var resolved = await GetEvents(reader, msg.EventStreamId, id, startEventNumber, endEventNumber, token);

		if (resolved.Count == 0)
			return StorageReaderWorker<string>.NoData(msg, ReadStreamResult.Success, lastIndexedPosition, msg.ValidationStreamVersion ?? 0);

		var records = resolved.OrderByDescending(x => x.OriginalEvent.EventNumber).ToArray();
		var isEndOfStream = startEventNumber == 0 || (startEventNumber <= lastEventNumber && (records.Length is 0 || records[^1].OriginalEventNumber != startEventNumber));
		long nextEventNumber = isEndOfStream ? -1 : Math.Min(startEventNumber - 1, lastEventNumber);

		return new(msg.CorrelationId, msg.EventStreamId, endEventNumber, msg.MaxCount,
			ReadStreamResult.Success, records, StreamMetadata.Empty, false, string.Empty,
			nextEventNumber, lastEventNumber, isEndOfStream, lastIndexedPosition);
	}

	async Task<ReadStreamEventsForwardCompleted> ReadForwards(
		ReadStreamEventsForward msg, IIndexReader<string> reader, long lastIndexedPosition, CancellationToken token
	) {
		var id = GetId(msg.EventStreamId);
		var lastEventNumber = GetLastNumber(id);

		if (msg.ValidationStreamVersion.HasValue && lastEventNumber == msg.ValidationStreamVersion)
			return StorageReaderWorker<string>.NoData(msg, ReadStreamResult.NotModified, lastIndexedPosition, msg.ValidationStreamVersion.Value);

		var fromEventNumber = msg.FromEventNumber < 0 ? 0 : msg.FromEventNumber;
		var maxCount = msg.MaxCount;
		var endEventNumber = fromEventNumber > long.MaxValue - maxCount + 1 ? long.MaxValue : fromEventNumber + maxCount - 1;
		var resolved = await GetEvents(reader, msg.EventStreamId, id, fromEventNumber, endEventNumber, token);

		if (resolved.Count == 0)
			return StorageReaderWorker<string>.NoData(msg, ReadStreamResult.Success, lastIndexedPosition, msg.ValidationStreamVersion ?? 0);

		long nextEventNumber = Math.Min(endEventNumber + 1, lastEventNumber + 1);
		if (resolved.Count > 0)
			nextEventNumber = resolved[^1].OriginalEventNumber + 1;
		var isEndOfStream = endEventNumber >= lastEventNumber;

		return new(msg.CorrelationId, msg.EventStreamId, msg.FromEventNumber, msg.MaxCount,
			ReadStreamResult.Success, resolved, StreamMetadata.Empty, false, string.Empty,
			nextEventNumber, lastEventNumber, isEndOfStream, lastIndexedPosition);
	}
}
