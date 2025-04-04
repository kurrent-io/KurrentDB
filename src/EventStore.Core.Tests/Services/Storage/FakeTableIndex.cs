// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Index;

namespace EventStore.Core.Tests.Services.Storage;

public class FakeTableIndex<TStreamId> : ITableIndex<TStreamId> {
	internal static readonly IndexEntry InvalidIndexEntry = new IndexEntry(0, -1, -1);
	public int ScavengeCount { get; private set; }

	public long PrepareCheckpoint {
		get { throw new NotImplementedException(); }
	}

	public long CommitCheckpoint {
		get { throw new NotImplementedException(); }
	}

	public void Initialize(long chaserCheckpoint) {
	}

	public void Close(bool removeFiles = true) {
	}

	public void Add(long commitPos, TStreamId streamId, long version, long position) {
		throw new NotImplementedException();
	}

	public void AddEntries(long commitPos, IList<IndexKey<TStreamId>> entries) {
		throw new NotImplementedException();
	}

	public bool TryGetOneValue(TStreamId streamId, long version, out long position) {
		position = -1;
		return false;
	}

	public bool TryGetLatestEntry(TStreamId streamId, out IndexEntry entry) {
		entry = InvalidIndexEntry;
		return false;
	}

	public ValueTask<IndexEntry?> TryGetLatestEntry(ulong stream, long beforePosition,
		Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream, CancellationToken token)
		=> ValueTask.FromException<IndexEntry?>(new NotImplementedException());

	public ValueTask<IndexEntry?> TryGetLatestEntry(TStreamId streamId, long beforePosition,
		Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream, CancellationToken token)
		=> ValueTask.FromException<IndexEntry?>(new NotImplementedException());

	public bool TryGetOldestEntry(TStreamId streamId, out IndexEntry entry) {
		entry = InvalidIndexEntry;
		return false;
	}

	public bool TryGetNextEntry(TStreamId streamId, long afterVersion, out IndexEntry entry) {
		throw new NotImplementedException();
	}

	public bool TryGetNextEntry(ulong stream, long afterVersion, out IndexEntry entry) {
		throw new NotImplementedException();
	}

	public bool TryGetPreviousEntry(TStreamId streamId, long beforeVersion, out IndexEntry entry) {
		throw new NotImplementedException();
	}

	public bool TryGetPreviousEntry(ulong stream, long beforeVersion, out IndexEntry entry) {
		throw new NotImplementedException();
	}

	public IEnumerable<ISearchTable> IterateAllInOrder() => throw new NotImplementedException();

	public IReadOnlyList<IndexEntry> GetRange(TStreamId streamId, long startVersion, long endVersion,
		int? limit = null) {
		return Array.Empty<IndexEntry>();
	}

	public IReadOnlyList<IndexEntry> GetRange(ulong stream, long startVersion, long endVersion, int? limit = null) {
		throw new NotImplementedException();
	}

	public ValueTask Scavenge(IIndexScavengerLog log, CancellationToken ct) {
		ScavengeCount++;
		return ValueTask.CompletedTask;
	}

	public ValueTask Scavenge(
		Func<IndexEntry, CancellationToken, ValueTask<bool>> shouldKeep,
		IIndexScavengerLog log,
		CancellationToken ct) {

		return Scavenge(log, ct);
	}

	public Task MergeIndexes() {
		return Task.CompletedTask;
	}

	public void WaitForBackgroundTasks(int millisecondsTimeout) {
		throw new NotImplementedException();
	}

	public bool IsBackgroundTaskRunning {
		get { return false; }
	}
}
