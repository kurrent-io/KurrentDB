// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Modules.Entities.Resolution;

/// <summary>
/// The fuzzy matcher's string metric, implemented in-repo instead of pulling a dependency:
/// token-sort indel similarity, the same formula as RapidFuzz's <c>token_sort_ratio</c> that
/// the reference pipeline uses. Word order washes out ("smith, john" matches "john smith"),
/// typos cost proportionally to length.
/// </summary>
public static class NameSimilarity {
	/// <summary>
	/// Similarity in [0, 1]: tokens of each input sorted and re-joined, then indel similarity
	/// over the results. Inputs are expected pre-normalized (see EntityName.Normalize).
	/// </summary>
	public static double TokenSortRatio(string left, string right) =>
		Ratio(SortTokens(left), SortTokens(right));

	/// <summary>
	/// Indel similarity in [0, 1]: <c>1 - distance/(|a|+|b|)</c> where distance counts
	/// insertions and deletions only (substitution = one deletion + one insertion).
	/// </summary>
	public static double Ratio(string left, string right) {
		if (left.Length == 0 && right.Length == 0)
			return 1.0;

		var lcs = LongestCommonSubsequenceLength(left, right);
		var distance = left.Length + right.Length - 2 * lcs;

		return 1.0 - (double)distance / (left.Length + right.Length);
	}

	static string SortTokens(string value) {
		var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		Array.Sort(tokens, StringComparer.Ordinal);
		return string.Join(' ', tokens);
	}

	// Classic two-row DP. Entity names are tens of characters, so O(m·n) is nothing.
	static int LongestCommonSubsequenceLength(string left, string right) {
		Span<int> previous = stackalloc int[right.Length + 1];
		Span<int> current  = stackalloc int[right.Length + 1];

		foreach (var leftChar in left) {
			for (var j = 0; j < right.Length; j++)
				current[j + 1] = leftChar == right[j]
					? previous[j] + 1
					: Math.Max(previous[j + 1], current[j]);

			current.CopyTo(previous);
			current.Clear();
		}

		return previous[right.Length];
	}
}
