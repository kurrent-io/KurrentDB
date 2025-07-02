// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentStorage.Utils.Extensions;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
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

	public void HandleStreanMetadataChange(ResolvedEvent evt) {
	}

	public long? GetLastPosition() {
		return !committed.IsEmpty() ? committed.Last().Event.LogPosition : null;
	}

	public void Commit() {
		lock (_lock) {
			committed.AddRange(_pending);
			_pending.Clear();
		}
	}

	public void Dispose() {
	}
}
