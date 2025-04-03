// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Index;

namespace EventStore.Core.Tests.Services.Storage;

public class FakeInMemoryTableIndex<TStreamId> : ITableIndex<TStreamId> {
	public long CommitCheckpoint => throw new NotImplementedException();

	public long PrepareCheckpoint => throw new NotImplementedException();

	public bool IsBackgroundTaskRunning => throw new NotImplementedException();

	private Dictionary<TStreamId, List<IndexKey<TStreamId>> > _indexEntries = new Dictionary<TStreamId, List<IndexKey<TStreamId>> >();
	public void Add(long commitPos, TStreamId streamId, long version, long position)
	{
		throw new NotImplementedException();
	}

	public void AddEntries(long commitPos, IList<IndexKey<TStreamId>> entries)
	{
		foreach(var entry in entries){
			if(!_indexEntries.ContainsKey(entry.StreamId))
				_indexEntries[entry.StreamId] = new List<IndexKey<TStreamId>>();
			_indexEntries[entry.StreamId].Add(entry);
		}
	}

	public void Close(bool removeFiles = true)
	{
	}

	public IEnumerable<ISearchTable> IterateAllInOrder() => throw new NotImplementedException();

	public IReadOnlyList<IndexEntry> GetRange(TStreamId streamId, long startVersion, long endVersion, int? limit = null)
	{
		var entries = new List<IndexEntry>();
		if(_indexEntries.ContainsKey(streamId)){
			foreach(var entry in _indexEntries[streamId]){
				if(startVersion <= entry.Version && entry.Version <= endVersion)
					entries.Add(new IndexEntry(entry.Hash, entry.Version, entry.Position));
			}
		}
		return entries;
	}

	public IReadOnlyList<IndexEntry> GetRange(ulong stream, long startVersion, long endVersion, int? limit = null) {
		throw new NotImplementedException();
	}

	public void Initialize(long chaserCheckpoint)
	{
	}

	public Task MergeIndexes()
	{
		throw new NotImplementedException();
	}

	public ValueTask Scavenge(IIndexScavengerLog log, CancellationToken ct)
		=> ValueTask.FromException(new NotImplementedException());

	public ValueTask Scavenge(
		Func<IndexEntry, CancellationToken, ValueTask<bool>> shouldKeep,
		IIndexScavengerLog log,
		CancellationToken ct)
		=> ValueTask.FromException(new NotImplementedException());

	public bool TryGetLatestEntry(TStreamId streamId, out IndexEntry entry)
	{
		if(_indexEntries.ContainsKey(streamId)){
			var entries = _indexEntries[streamId];
			var lastEntry = entries[entries.Count - 1];
			entry = new IndexEntry(lastEntry.Hash, lastEntry.Version, lastEntry.Position);
			return true;
		}
		else{
			entry = new IndexEntry();
			return false;
		}
	}

	public ValueTask<IndexEntry?> TryGetLatestEntry(ulong stream, long beforePosition,
		Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream, CancellationToken token)
		=> ValueTask.FromException<IndexEntry?>(new NotImplementedException());

	public ValueTask<IndexEntry?> TryGetLatestEntry(TStreamId stream, long beforePosition,
		Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream, CancellationToken token)
		=> ValueTask.FromException<IndexEntry?>(new NotImplementedException());

	public bool TryGetNextEntry(TStreamId streamId, long afterVersion, out IndexEntry entry)
	{
		throw new NotImplementedException();
	}

	public bool TryGetNextEntry(ulong stream, long afterVersion, out IndexEntry entry)
	{
		throw new NotImplementedException();
	}

	public bool TryGetPreviousEntry(TStreamId streamId, long beforeVersion, out IndexEntry entry)
	{
		throw new NotImplementedException();
	}

	public bool TryGetPreviousEntry(ulong stream, long beforeVersion, out IndexEntry entry)
	{
		throw new NotImplementedException();
	}

	public bool TryGetOldestEntry(TStreamId streamId, out IndexEntry entry)
	{
		throw new NotImplementedException();
	}

	public bool TryGetOneValue(TStreamId streamId, long version, out long position)
	{
		throw new NotImplementedException();
	}

	public void WaitForBackgroundTasks(int millisecondsTimeout) {
		throw new NotImplementedException();
	}
}
