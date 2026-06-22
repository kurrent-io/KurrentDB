// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Search;

public static partial class SearchAlgorithms {
	public static float TokenJaccard(HashSet<string> a, HashSet<string> b) {
		if (a.Count == 0 || b.Count == 0)
			return 0f;
		var intersection = 0;
		var smaller = a.Count <= b.Count ? a : b;
		var larger = a.Count <= b.Count ? b : a;
		foreach (var token in smaller)
			if (larger.Contains(token))
				intersection++;
		var union = a.Count + b.Count - intersection;
		return union > 0 ? (float)intersection / union : 0f;
	}
}