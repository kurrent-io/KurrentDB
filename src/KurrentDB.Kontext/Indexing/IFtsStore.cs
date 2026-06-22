// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;

namespace KurrentDB.Kontext.Indexing;

public interface IFtsStore : IIndexStore, IDisposable {
	void Add(ulong id, TFPos position, string keys, string values, bool forceUpdate = false);
	void Refresh();
	IEnumerable<ulong> Search(IReadOnlyList<string> keywords, IReadOnlyList<string> excludeWords, int limit);
	IEnumerable<ulong> List(int limit);
}
