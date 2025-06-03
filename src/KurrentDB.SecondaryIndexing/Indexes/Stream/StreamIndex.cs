// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Stream;

internal class StreamIndex(DuckDbDataSource db, IReadIndex<string> readIndex) {
	public long? GetLastPosition() => Processor.LastCommittedPosition;

	public StreamIndexProcessor Processor { get; } = new(db, readIndex.IndexReader.Backend);

	public IReadOnlyList<IVirtualStreamReader> Readers { get; } = [];
}
