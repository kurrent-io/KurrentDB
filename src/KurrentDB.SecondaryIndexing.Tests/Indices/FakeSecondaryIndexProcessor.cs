// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentStorage.Utils.Extensions;
using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Indices;

namespace KurrentDB.SecondaryIndexing.Tests.Indices;

public class FakeSecondaryIndexProcessor(IList<ResolvedEvent> committed, IList<ResolvedEvent>? pending = null): ISecondaryIndexProcessor {
	private readonly object _lock = new();
	private readonly IList<ResolvedEvent> _pending = pending ?? [];

	public ValueTask Index(ResolvedEvent resolvedEvent, CancellationToken token = default) {
		lock (_lock) {
			_pending.Add(resolvedEvent);
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask Commit(CancellationToken token = default) {
		lock (_lock) {
			committed.AddRange(_pending);
			_pending.Clear();
		}
		return ValueTask.CompletedTask;
	}
}
