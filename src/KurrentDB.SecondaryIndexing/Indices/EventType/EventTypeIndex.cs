// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indices.EventType;

internal static class EventTypeIndexConstants {
	public const string IndexPrefix = $"{SystemStreams.IndexStreamPrefix}et-";
}

internal class EventTypeIndex<TStreamId> : ISecondaryIndex {
	private readonly EventTypeIndexProcessor _processor;

	public EventTypeIndex(DuckDbDataSource db, DuckDBAdvancedConnection connection, IReadIndex<TStreamId> readIndex) {
		_processor = new EventTypeIndexProcessor(connection);
		Readers = [new EventTypeIndexReader<TStreamId>(db, _processor, readIndex)];
	}

	public void Init() {
	}

	public ulong? GetLastPosition() =>
		(ulong)_processor.LastCommittedPosition;

	public ulong? GetLastSequence() => (ulong)_processor.Seq;

	public ISecondaryIndexProcessor Processor => _processor;
	public IReadOnlyList<IVirtualStreamReader> Readers { get; }

	public SequenceRecord LastIndexed => _processor.LastIndexed;

	public void Dispose() { }
}
