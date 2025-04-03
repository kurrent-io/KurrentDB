// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.Index;

public interface ISearchTable {
	Guid Id { get; }
	long Count { get; }
	byte Version { get; }

	bool TryGetOneValue(ulong stream, long number, out long position);
	bool TryGetLatestEntry(ulong stream, out IndexEntry entry);
	ValueTask<IndexEntry?> TryGetLatestEntry(ulong stream, long beforePosition, Func<IndexEntry, CancellationToken, ValueTask<bool>> isForThisStream, CancellationToken token);
	bool TryGetOldestEntry(ulong stream, out IndexEntry entry);
	bool TryGetNextEntry(ulong stream, long afterVersion, out IndexEntry entry);
	bool TryGetPreviousEntry(ulong stream, long beforeVersion, out IndexEntry entry);
	IReadOnlyList<IndexEntry> GetRange(ulong stream, long startNumber, long endNumber, int? limit = null);
	IEnumerable<IndexEntry> IterateAllInOrder();
}
