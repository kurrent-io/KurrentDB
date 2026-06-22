// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Search;

public static partial class SearchAlgorithms {
	public static IReadOnlyList<ScoredDoc> MmrSelect(
		IReadOnlyList<ScoredDoc> candidates,
		IReadOnlyDictionary<ulong, HydratedDoc> docs,
		int topK) {
		if (candidates.Count <= topK)
			return candidates;

		var selectedDocs = new Dictionary<ulong, HydratedDoc>(topK);
		var selectedOrder = new List<ScoredDoc>(topK);

		for (var i = 0; i < topK; i++) {
			ulong bestId = 0;
			float bestRelevance = 0;
			var bestMmr = float.NegativeInfinity;

			foreach (var (docId, relevance) in candidates) {
				if (selectedDocs.ContainsKey(docId))
					continue;

				var unselectedDoc = docs[docId];
				float maxSimilarity = 0;
				foreach (var selectedDoc in selectedDocs.Values) {
					var similarity = Similarity(unselectedDoc, selectedDoc);
					if (similarity > maxSimilarity)
						maxSimilarity = similarity;
				}

				var mmr = MmrLambda * relevance - (1 - MmrLambda) * maxSimilarity;
				if (mmr > bestMmr) {
					bestMmr = mmr;
					bestId = docId;
					bestRelevance = relevance;
				}
			}

			selectedDocs[bestId] = docs[bestId];
			selectedOrder.Add(new(bestId, bestRelevance));
		}

		return selectedOrder;
	}

	/// <summary>
	/// Pairwise similarity for MMR's diversity term. Prefers embedding cosine when both
	/// docs have embeddings; falls back to token-set Jaccard on the hydrated passage when
	/// embeddings aren't available (e.g., no vector store configured). Returns 0 when
	/// neither signal is present, which makes MMR degenerate gracefully to pure relevance.
	/// </summary>
	public static float Similarity(HydratedDoc a, HydratedDoc b) {
		if (!a.Embedding.IsEmpty && !b.Embedding.IsEmpty)
			return DotProduct(a.Embedding.Span, b.Embedding.Span);
		if (a.Tokens != null && b.Tokens != null)
			return TokenJaccard(a.Tokens, b.Tokens);
		return 0f;
	}
}