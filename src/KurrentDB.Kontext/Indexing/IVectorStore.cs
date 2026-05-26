// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;

namespace KurrentDB.Kontext.Indexing;

public interface IVectorStore : IIndexStore, IDisposable {
	void Add(ulong id, TFPos position, ReadOnlyMemory<float> vector);
	IEnumerable<ulong> Search(ReadOnlyMemory<float> query, int limit);
	bool TryGet(ulong id, out ReadOnlyMemory<float> vector);
}
