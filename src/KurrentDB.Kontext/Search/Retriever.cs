// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Indexing;
using KurrentDB.Kontext.Workspaces;

namespace KurrentDB.Kontext.Search;

public class Retriever(
	StoreRegistry<IFtsStore> ftsStores,
	StoreRegistry<IVectorStore> vecStores) {

	public IReadOnlyList<ScoredDoc> HybridSearch(
		WorkspaceIndex index,
		IReadOnlyList<string> keywords,
		IReadOnlyList<string> excludeWords,
		ReadOnlyMemory<float> embedding,
		int fetchCount) {
		var scores = new Dictionary<ulong, float>();

		if (ftsStores.TryGet(index, out var fts)) {
			var bm25Hits = fts!.Search(keywords, excludeWords, fetchCount);
			SearchAlgorithms.AccumulateRrfScores(bm25Hits, scores);
		}

		if (vecStores.TryGet(index, out var vec)) {
			var knnHits = vec!.Search(embedding, fetchCount);
			SearchAlgorithms.AccumulateRrfScores(knnHits, scores);
		}

		return SearchAlgorithms.NormalizeRrfScores(scores);
	}

	public IReadOnlyList<ScoredDoc> ListDocs(WorkspaceIndex index, int limit) {
		if (!ftsStores.TryGet(index, out var fts))
			return [];

		var result = new List<ScoredDoc>(limit);
		foreach (var id in fts!.List(limit))
			result.Add(new ScoredDoc(id, 0f));

		return result;
	}

	public bool TryAttachEmbeddings(WorkspaceIndex index, Dictionary<ulong, HydratedDoc> docs) {
		if (!vecStores.TryGet(index, out var vec))
			return false;

		foreach (var (docId, doc) in docs)
			if (vec!.TryGet(docId, out var vector))
				doc.Embedding = vector;
		return true;
	}
}
