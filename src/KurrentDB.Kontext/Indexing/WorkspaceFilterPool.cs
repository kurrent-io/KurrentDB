// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Collections.Concurrent;
using KurrentDB.Kontext.Workspaces.ControlPlane;

namespace KurrentDB.Kontext.Indexing;

public sealed class WorkspaceFilterPool(IReadOnlyList<FilterRule> rules) {
	const int Capacity = 16;

	readonly WorkspaceFilter?[] _filters = new WorkspaceFilter?[Capacity];
	readonly IndexPool _slots = new(Capacity);

	public Lease Rent() =>
		_slots.TryGet(out var slot)
			? new Lease(this, slot, _filters[slot] ??= WorkspaceFilter.Compile(rules))
			: new Lease(this, slot: -1, WorkspaceFilter.Compile(rules));

	void Return(int slot) {
		if (slot >= 0)
			_slots.Return(slot);
	}

	public readonly struct Lease(WorkspaceFilterPool pool, int slot, WorkspaceFilter filter) : IDisposable {
		public WorkspaceFilter Filter => filter;
		public void Dispose() => pool.Return(slot);
	}
}
