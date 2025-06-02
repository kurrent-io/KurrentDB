// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Stream;

internal class StreamIndex<TStreamId>(DuckDbDataSource db, IReadIndex<TStreamId> readIndex) : ISecondaryIndex {
	public void Init() {
	}

	public ulong? GetLastPosition() => (ulong)Processor.LastCommittedPosition;

	public ulong? GetLastSequence() => (ulong)Processor.Seq;

	public StreamIndexProcessor<TStreamId> Processor { get; } = new(db, readIndex.IndexReader.Backend);

	public IReadOnlyList<IVirtualStreamReader> Readers { get; } = [];

	public void Commit() => Processor.Commit();

	public void Dispose() { }
}
