// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Exceptions;

namespace EventStore.Core.Index;

public class HashListMemTable : IMemTable, ISearchTable {
	private static readonly IComparer<Entry> MemTableComparer = new EventNumberComparer();
	private static readonly IComparer<Entry> LogPosComparer = new LogPositionComparer();

	public long Count {
		get { return _count; }
	}

	public Guid Id {
		get { return _id; }
	}

	public byte Version {
		get { return _version; }
	}

	private readonly ConcurrentDictionary<ulong, SortedList<Entry, byte>> _hash;
	private readonly Guid _id = Guid.NewGuid();
	private readonly byte _version;
	private int _count;

	private int _isConverting;

	public HashListMemTable(byte version, int maxSize) {
		_version = version;
		_hash = new ConcurrentDictionary<ulong, SortedList<Entry, byte>>();
	}

	public bool MarkForConversion() {
		return Interlocked.CompareExchange(ref _isConverting, 1, 0) == 0;
	}

	public void Add(ulong stream, long version, long position) {
		AddEntries(new[] { new IndexEntry(stream, version, position) });
	}

	public void AddEntries(IList<IndexEntry> entries) {
		Ensure.NotNull(entries, "entries");
		Ensure.Positive(entries.Count, "entries.Count");

		var collection = entries.Select(x => new IndexEntry(GetHash(x.Stream), x.Version, x.Position)).ToList();

		// only one thread at a time can write
		Interlocked.Add(ref _count, collection.Count);

		var stream = collection[0].Stream; // NOTE: all entries should have the same stream
		SortedList<Entry, byte> list = null;
		try {

			if (!_hash.TryGetValue(stream, out list)) {
				list = new SortedList<Entry, byte>(MemTableComparer);
				if (!Monitor.TryEnter(list, 10000))
					throw new UnableToAcquireLockInReasonableTimeException();
				_hash.AddOrUpdate(stream, list,
					(x, y) => {
						throw new Exception("This should never happen as MemTable updates are single-threaded.");
					});
			} else {
				if (!Monitor.TryEnter(list, 10000))
					throw new UnableToAcquireLockInReasonableTimeException();
			}

			for (int i = 0, n = collection.Count; i < n; ++i) {
				var entry = collection[i];
				if (entry.Stream != stream)
					throw new Exception("Not all index entries in a bulk have the same stream hash.");
				Ensure.Nonnegative(entry.Version, "entry.Version");
				Ensure.Nonnegative(entry.Position, "entry.Position");
				list.Add(new Entry(entry.Version, entry.Position), 0);
			}
		} finally {
			if (list != null)
				Monitor.Exit(list);
		}
	}

	public bool TryGetOneValue(ulong stream, long number, out long position) {
		if (number < 0)
			throw new ArgumentOutOfRangeException("number");
		ulong hash = GetHash(stream);

		position = 0;

		SortedList<Entry, byte> list;
		if (_hash.TryGetValue(hash, out list)) {
			if (!Monitor.TryEnter(list, 10000))
				throw new UnableToAcquireLockInReasonableTimeException();
			try {
				int endIdx = list.UpperBound(new Entry(number, long.MaxValue));
				if (endIdx == -1)
					return false;

				var key = list.Keys[endIdx];
				if (key.EvNum == number) {
					position = key.LogPos;
					return true;
				}
			} finally {
				Monitor.Exit(list);
			}
		}

		return false;
	}

	public bool TryGetLatestEntry(ulong stream, out IndexEntry entry) {
		ulong hash = GetHash(stream);
		entry = TableIndex.InvalidIndexEntry;

		SortedList<Entry, byte> list;
		if (_hash.TryGetValue(hash, out list)) {
			if (!Monitor.TryEnter(list, 10000))
				throw new UnableToAcquireLockInReasonableTimeException();
			try {
				var latest = list.Keys[list.Count - 1];
				entry = new IndexEntry(hash, latest.EvNum, latest.LogPos);
				return true;
			} finally {
				Monitor.Exit(list);
			}
		}

		return false;
	}

	public bool TryGetLatestEntry(ulong stream, long beforePosition, Func<IndexEntry, bool> isForThisStream, out IndexEntry entry) {
		if (beforePosition < 0)
			throw new ArgumentOutOfRangeException(nameof(beforePosition));

		ulong hash = GetHash(stream);
		entry = TableIndex.InvalidIndexEntry;

		if (!_hash.TryGetValue(hash, out var list))
			return false;

		if (!Monitor.TryEnter(list, 10000))
			throw new UnableToAcquireLockInReasonableTimeException();

		try {
			// we use LogPosComparer here so that it only compares the position part of the key we
			// are passing in and not the evNum (maxvalue) which is meaningless
			int endIdx = list.UpperBound(
				key: new Entry(long.MaxValue, beforePosition - 1),
				comparer: LogPosComparer,
				continueSearch: e => isForThisStream(new IndexEntry(hash, e.EvNum, e.LogPos)));

			if (endIdx == -1)
				return false;

			var latestBeforePosition = list.Keys[endIdx];
			entry = new IndexEntry(hash, latestBeforePosition.EvNum, latestBeforePosition.LogPos);
			return true;
		} catch (SearchStoppedException) {
			// fall back to linear search if there was a hash collision
			int maxIdx = list.FindMax(e =>
				e.LogPos < beforePosition &&
				isForThisStream(new IndexEntry(hash, e.EvNum, e.LogPos)));

			if (maxIdx == -1)
				return false;

			var latestBeforePosition = list.Keys[maxIdx];
			entry = new IndexEntry(hash, latestBeforePosition.EvNum, latestBeforePosition.LogPos);
			return true;
		} finally {
			Monitor.Exit(list);
		}
	}

	public bool TryGetOldestEntry(ulong stream, out IndexEntry entry) {
		ulong hash = GetHash(stream);
		entry = TableIndex.InvalidIndexEntry;

		SortedList<Entry, byte> list;
		if (_hash.TryGetValue(hash, out list)) {
			if (!Monitor.TryEnter(list, 10000))
				throw new UnableToAcquireLockInReasonableTimeException();
			try {
				var oldest = list.Keys[0];
				entry = new IndexEntry(hash, oldest.EvNum, oldest.LogPos);
				return true;
			} finally {
				Monitor.Exit(list);
			}
		}

		return false;
	}

	public bool TryGetNextEntry(ulong stream, long afterNumber, out IndexEntry entry) {
		if (afterNumber < 0)
			throw new ArgumentOutOfRangeException(nameof(afterNumber));

		ulong hash = GetHash(stream);
		entry = TableIndex.InvalidIndexEntry;

		if (afterNumber >= long.MaxValue)
			return false;

		SortedList<Entry, byte> list;
		if (_hash.TryGetValue(hash, out list)) {
			if (!Monitor.TryEnter(list, 10000))
				throw new UnableToAcquireLockInReasonableTimeException();
			try {
				int endIdx = list.LowerBound(new Entry(afterNumber + 1, 0));
				if (endIdx == -1)
					return false;

				var e = list.Keys[endIdx];
				entry = new IndexEntry(hash, e.EvNum, e.LogPos);
				return true;
			} finally {
				Monitor.Exit(list);
			}
		}

		return false;
	}

	public bool TryGetPreviousEntry(ulong stream, long beforeNumber, out IndexEntry entry) {
		if (beforeNumber < 0)
			throw new ArgumentOutOfRangeException(nameof(beforeNumber));

		ulong hash = GetHash(stream);
		entry = TableIndex.InvalidIndexEntry;

		if (beforeNumber <= 0)
			return false;

		SortedList<Entry, byte> list;
		if (_hash.TryGetValue(hash, out list)) {
			if (!Monitor.TryEnter(list, 10000))
				throw new UnableToAcquireLockInReasonableTimeException();
			try {
				int endIdx = list.UpperBound(new Entry(beforeNumber - 1, long.MaxValue));
				if (endIdx == -1)
					return false;

				var e = list.Keys[endIdx];
				entry = new IndexEntry(hash, e.EvNum, e.LogPos);
				return true;
			} finally {
				Monitor.Exit(list);
			}
		}

		return false;
	}

	public IEnumerable<IndexEntry> IterateAllInOrder() {
		//Log.Trace("Sorting array in HashListMemTable.IterateAllInOrder...");

		var keys = _hash.Keys.ToArray();
		Array.Sort(keys, new ReverseComparer<ulong>());

		foreach (var key in keys) {
			var list = _hash[key];
			for (int i = list.Count - 1; i >= 0; --i) {
				var x = list.Keys[i];
				yield return new IndexEntry(key, x.EvNum, x.LogPos);
			}
		}

		//Log.Trace("Sorting array in HashListMemTable.IterateAllInOrder... DONE!");
	}

	public void Clear() {
		_hash.Clear();
	}

	public IList<IndexEntry> GetRange(ulong stream, long startNumber, long endNumber, int? limit = null) {
		if (startNumber < 0)
			throw new ArgumentOutOfRangeException("startNumber");
		if (endNumber < 0)
			throw new ArgumentOutOfRangeException("endNumber");

		ulong hash = GetHash(stream);
		var ret = new List<IndexEntry>();

		SortedList<Entry, byte> list;
		if (_hash.TryGetValue(hash, out list)) {
			if (!Monitor.TryEnter(list, 10000))
				throw new UnableToAcquireLockInReasonableTimeException();
			try {
				var endIdx = list.UpperBound(new Entry(endNumber, long.MaxValue));
				for (int i = endIdx; i >= 0; i--) {
					var key = list.Keys[i];
					if (key.EvNum < startNumber || ret.Count == limit)
						break;
					ret.Add(new IndexEntry(hash, version: key.EvNum, position: key.LogPos));
				}
			} finally {
				Monitor.Exit(list);
			}
		}

		return ret;
	}

	private ulong GetHash(ulong hash) {
		return _version == PTableVersions.IndexV1 ? hash >> 32 : hash;
	}

	private struct Entry {
		public readonly long EvNum;
		public readonly long LogPos;

		public Entry(long evNum, long logPos) {
			EvNum = evNum;
			LogPos = logPos;
		}
	}

	private class EventNumberComparer : IComparer<Entry> {
		public int Compare(Entry x, Entry y) {
			if (x.EvNum < y.EvNum)
				return -1;
			if (x.EvNum > y.EvNum)
				return 1;
			if (x.LogPos < y.LogPos)
				return -1;
			if (x.LogPos > y.LogPos)
				return 1;
			return 0;
		}
	}

	private class LogPositionComparer : IComparer<Entry> {
		public int Compare(Entry x, Entry y) {
			if (x.LogPos < y.LogPos)
				return -1;
			if (x.LogPos > y.LogPos)
				return 1;
			return 0;
		}
	}
}

public class ReverseComparer<T> : IComparer<T> where T : IComparable {
	public int Compare(T x, T y) {
		return -x.CompareTo(y);
	}
}
