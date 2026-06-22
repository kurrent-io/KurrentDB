// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Indexing.USearch;

sealed class NearestMatches {
	readonly Dictionary<ulong, float> _byId = [];

	public void Offer(ulong id, float distance) {
		if (!_byId.TryGetValue(id, out var existing) || distance < existing)
			_byId[id] = distance;
	}

	public List<ulong> Nearest(int limit) {
		var ranked = new List<KeyValuePair<ulong, float>>(_byId);
		ranked.Sort(static (a, b) => a.Value.CompareTo(b.Value));

		var take = Math.Min(limit, ranked.Count);
		var ids = new List<ulong>(take);
		for (var i = 0; i < take; i++)
			ids.Add(ranked[i].Key);
		return ids;
	}
}
