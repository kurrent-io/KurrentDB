// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;

namespace EventStore.Core.Index;

public interface ISearchTable {
	Guid Id { get; }
	long Count { get; }
	byte Version { get; }

	bool TryGetOneValue(ulong stream, long number, out long position);
	bool TryGetLatestEntry(ulong stream, out IndexEntry entry);
	bool TryGetLatestEntry(ulong stream, long beforePosition, Func<IndexEntry, bool> isForThisStream, out IndexEntry entry);
	bool TryGetOldestEntry(ulong stream, out IndexEntry entry);
	bool TryGetNextEntry(ulong stream, long afterVersion, out IndexEntry entry);
	bool TryGetPreviousEntry(ulong stream, long beforeVersion, out IndexEntry entry);
	IList<IndexEntry> GetRange(ulong stream, long startNumber, long endNumber, int? limit = null);
	IEnumerable<IndexEntry> IterateAllInOrder();
}
