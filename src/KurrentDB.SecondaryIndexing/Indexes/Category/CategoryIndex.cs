// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Indexes.Category;

internal static class CategoryIndexConstants {
	public const string IndexPrefix = $"{SystemStreams.IndexStreamPrefix}ce-";
}

internal class CategoryIndex<TStreamId> : ISecondaryIndex {
	public CategoryIndex(DuckDbDataSource db, IReadIndex<TStreamId> readIndex) {
		Processor = new(db);
		Readers = [new CategoryIndexReader<TStreamId>(db, Processor, readIndex)];
	}

	public void Init() {
	}

	public ulong? GetLastPosition() => (ulong)Processor.LastCommittedPosition;

	public CategoryIndexProcessor Processor { get; }

	public IReadOnlyList<IVirtualStreamReader> Readers { get; }

	public void Commit() => Processor.Commit();

	public void Dispose() { }
}
