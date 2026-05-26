// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Search;

public static partial class SearchAlgorithms {
	/// <summary>
	/// Accumulate RRF scores: 1/(k + rank) for each result list.
	/// </summary>
	public static void AccumulateRrfScores(
		IEnumerable<ulong> docIds,
		Dictionary<ulong, float> scores) {
		int rank = 1;
		foreach (var docId in docIds) {
			scores[docId] = scores.GetValueOrDefault(docId, 0f) + 1.0f / (RrfK + rank);
			rank++;
		}
	}

	/// <summary>
	/// Index-based overload: accumulates RRF scores from the first <paramref name="limit"/>
	/// entries of an already-ranked list.
	/// </summary>
	public static void AccumulateRrfScores(
		IReadOnlyList<ScoredDoc> docs,
		Dictionary<ulong, float> scores,
		int limit) {
		var lim = Math.Min(limit, docs.Count);
		for (var i = 0; i < lim; i++) {
			var docId = docs[i].DocId;
			scores[docId] = scores.GetValueOrDefault(docId, 0f) + 1.0f / (RrfK + i + 1);
		}
	}

	/// <summary>
	/// Normalize RRF scores to 0-1 range and return sorted candidates.
	/// </summary>
	public static IReadOnlyList<ScoredDoc> NormalizeRrfScores(Dictionary<ulong, float> scores) {
		var maxScore = 2.0f / (RrfK + 1);
		var result = new List<ScoredDoc>(scores.Count);
		foreach (var kvp in scores)
			result.Add(new(kvp.Key, kvp.Value / maxScore));
		result.Sort(static (a, b) => b.Score.CompareTo(a.Score));
		return result;
	}
}