// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.Index;

public interface ITableIndex {
	long CommitCheckpoint { get; }
	long PrepareCheckpoint { get; }

	void Initialize(long chaserCheckpoint);
	void Close(bool removeFiles = true);

	// this overload keeps IndexEntries that exist in the log
	ValueTask Scavenge(IIndexScavengerLog log, CancellationToken ct);
	// this overload keeps IndexEntries that pass the keep predicate
	ValueTask Scavenge(Func<IndexEntry, CancellationToken, ValueTask<bool>> shouldKeep, IIndexScavengerLog log, CancellationToken ct);
	Task MergeIndexes();
	IEnumerable<ISearchTable> IterateAllInOrder();
	bool IsBackgroundTaskRunning { get; }
}

public interface ITableIndex<TStreamId> : ITableIndex {
	void Add(long commitPos, TStreamId streamId, long version, long position);
	void AddEntries(long commitPos, IList<IndexKey<TStreamId>> entries);

	bool TryGetOneValue(TStreamId streamId, long version, out long position);
	bool TryGetLatestEntry(TStreamId streamId, out IndexEntry entry);
	ValueTask<IndexEntry?> TryGetLatestEntry(ulong stream, long beforePosition, Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream, CancellationToken token);
	ValueTask<IndexEntry?> TryGetLatestEntry(TStreamId streamId, long beforePosition, Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream, CancellationToken token);
	bool TryGetOldestEntry(TStreamId streamId, out IndexEntry entry);
	bool TryGetNextEntry(TStreamId streamId, long afterVersion, out IndexEntry entry);
	bool TryGetNextEntry(ulong stream, long afterVersion, out IndexEntry entry);
	bool TryGetPreviousEntry(TStreamId streamId, long beforeVersion, out IndexEntry entry);
	bool TryGetPreviousEntry(ulong stream, long beforeVersion, out IndexEntry entry);

	IReadOnlyList<IndexEntry> GetRange(TStreamId streamId, long startVersion, long endVersion, int? limit = null);
	IReadOnlyList<IndexEntry> GetRange(ulong stream, long startVersion, long endVersion, int? limit = null);

	void WaitForBackgroundTasks(int millisecondsTimeout);
}
