// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentStorage.Utils.Extensions;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Diagnostics;
using KurrentDB.SecondaryIndexing.Indexes;

namespace KurrentDB.SecondaryIndexing.Tests.Fakes;

public class FakeSecondaryIndexProcessor(IList<ResolvedEvent> committed, IList<ResolvedEvent>? pending = null)
	: ISecondaryIndexProcessor {
	private readonly object _lock = new();
	private readonly IList<ResolvedEvent> _pending = pending ?? [];

	public void Index(ResolvedEvent resolvedEvent) {
		lock (_lock) {
			_pending.Add(resolvedEvent);
		}
	}

	public (TFPos, long, DateTimeOffset) GetLastPosition() {
		if (committed.IsEmpty()) return (TFPos.Invalid, 0, DateTimeOffset.MinValue);

		// This won't work because of RowId, but it's not used yet
		var last = committed.Last();
		return (last.EventPosition ?? TFPos.Invalid, 0, last.Event.TimeStamp);
	}

	public SecondaryIndexProgressTracker Tracker { get; } = new("fake", new("fake"), -1, DateTime.MinValue);

	public TFPos LastIndexedPosition => committed.IsEmpty() ? TFPos.Invalid : committed.Last().EventPosition ?? TFPos.Invalid;

	public void Commit() {
		lock (_lock) {
			committed.AddRange(_pending);
			_pending.Clear();
		}
	}

	public void Dispose() {
	}
}
