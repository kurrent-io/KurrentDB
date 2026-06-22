// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using KurrentDB.Kontext.Workspaces;

namespace KurrentDB.Kontext.Indexing;

public sealed class StoreRegistry<TStore> where TStore : class {
	readonly ConcurrentDictionary<WorkspaceIndex, TStore> _stores = new();

	public bool TryAdd(WorkspaceIndex index, TStore store) => _stores.TryAdd(index, store);

	public bool TryRemove(WorkspaceIndex index, out TStore? store) => _stores.TryRemove(index, out store);

	public bool TryGet(WorkspaceIndex index, out TStore? store) => _stores.TryGetValue(index, out store);
}