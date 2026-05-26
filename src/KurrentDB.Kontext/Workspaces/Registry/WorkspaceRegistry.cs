// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using KurrentDB.Kontext.Workspaces.ControlPlane;

namespace KurrentDB.Kontext.Workspaces.Registry;

public sealed class WorkspaceRegistry {
	readonly ConcurrentDictionary<string, WorkspaceEntry> _entries = new(StringComparer.Ordinal);

	// Copy-on-write snapshot of the entries, rebuilt only on add/remove
	volatile WorkspaceEntry[] _all = [];

	public IReadOnlyList<WorkspaceEntry> All => _all;

	public bool TryGet(string name, out WorkspaceEntry entry) =>
		_entries.TryGetValue(name, out entry!);

	public bool TryGetRunning(string name, out WorkspaceEntry entry) =>
		TryGet(name, out entry) && entry.Status == WorkspaceLifecycle.Started;

	internal void Upsert(WorkspaceEntry entry) {
		_entries[entry.Name] = entry;
		_all = [.._entries.Values];
	}

	internal void Remove(string name) {
		if (_entries.TryRemove(name, out _))
			_all = [.._entries.Values];
	}

	internal void SetStatus(string name, WorkspaceLifecycle status) {
		if (_entries.TryGetValue(name, out var entry))
			entry.Status = status;
	}
}
