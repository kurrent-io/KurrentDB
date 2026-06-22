// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;

namespace KurrentDB.Kontext.Tests.Fakes;

// Position-free overloads for tests that don't care about durability watermarks.
static class IndexStoreTestExtensions {
	public static void Add(this IVectorStore store, ulong id, ReadOnlyMemory<float> vector) =>
		store.Add(id, new TFPos((long)id, (long)id), vector);

	// Persists everything pending, like the old void Flush().
	public static void Flush(this IVectorStore store) =>
		store.Flush(default, force: true);

	public static void Add(this IFtsStore store, ulong id, string keys, string values, bool forceUpdate = false) =>
		store.Add(id, new TFPos((long)id, (long)id), keys, values, forceUpdate);

	// Persists everything pending, like the old void Commit().
	public static void Commit(this IFtsStore store) =>
		store.Flush(default, force: true);
}
