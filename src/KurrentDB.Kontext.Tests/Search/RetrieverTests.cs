// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Indexing.USearch;
using KurrentDB.Kontext.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Tests.Search;

public class RetrieverTests : IDisposable {
	readonly StoreRegistry<IFtsStore> _ftsStores;
	readonly StoreRegistry<IVectorStore> _vecStores;
	readonly Dictionary<WorkspaceIndex, IFtsStore> _ownedFts = new();
	readonly Dictionary<WorkspaceIndex, IVectorStore> _ownedVec = new();
	readonly VectorIndexMetadata _meta;
	readonly Retriever _retriever;
	readonly string _dataPath;

	static WorkspaceIndex Idx(string workspace) => new(workspace, IndexKind.Events);

	public RetrieverTests() {
		_dataPath = Path.Combine(Path.GetTempPath(), $"kurrent_kontext_retriever_test_{Guid.NewGuid():N}");
		Directory.CreateDirectory(_dataPath);
		Directory.CreateDirectory(Path.Combine(_dataPath, "fts"));
		Directory.CreateDirectory(Path.Combine(_dataPath, "embeddings"));
		_meta = new VectorIndexMetadata("Test", "test-model", EmbeddingService.Dimensions);
		_ftsStores = new StoreRegistry<IFtsStore>();
		_vecStores = new StoreRegistry<IVectorStore>();
		_retriever = new Retriever(_ftsStores, _vecStores);
		Ensure("test");
	}

	public void Dispose() {
		foreach (var (key, s) in _ownedFts) { _ftsStores.TryRemove(key, out _); s.Dispose(); }
		foreach (var (key, s) in _ownedVec) { _vecStores.TryRemove(key, out _); s.Dispose(); }
		if (Directory.Exists(_dataPath))
			Directory.Delete(_dataPath, recursive: true);
	}

	void Ensure(string workspace) {
		var idx = Idx(workspace);
		if (!_ownedFts.ContainsKey(idx)) {
			var fts = new LuceneFtsStore(Path.Combine(_dataPath, "fts"), workspace, NullLogger<LuceneFtsStore>.Instance);
			_ownedFts[idx] = fts;
			_ftsStores.TryAdd(idx, fts);
		}
		if (!_ownedVec.ContainsKey(idx)) {
			var vec = new USearchVectorStore(Path.Combine(_dataPath, "embeddings"), workspace, _meta,
				NullLogger<USearchVectorStore>.Instance);
			_ownedVec[idx] = vec;
			_vecStores.TryAdd(idx, vec);
		}
	}

	IFtsStore Fts(string workspace) { Ensure(workspace); return _ownedFts[Idx(workspace)]; }
	IVectorStore Vec(string workspace) { Ensure(workspace); return _ownedVec[Idx(workspace)]; }

	void Index(ulong preparePosition, string content, float[]? embedding = null, string workspace = "test") {
		var fts = Fts(workspace);
		fts.Add(preparePosition, content, content);
		fts.Refresh();
		Vec(workspace).Add(preparePosition, embedding ?? MakeEmbedding(0));
	}

	static float[] MakeEmbedding(int seed) {
		var rng = new Random(seed);
		var emb = new float[EmbeddingService.Dimensions];
		for (var i = 0; i < emb.Length; i++)
			emb[i] = (float)(rng.NextDouble() * 2 - 1);
		var norm = MathF.Sqrt(emb.Sum(x => x * x));
		if (norm > 0)
			for (var i = 0; i < emb.Length; i++)
				emb[i] /= norm;
		return emb;
	}

	// -------- Fixture sanity --------

	[Test]
	public async Task Fixture_Ensure_Registers_Both_Stores() {
		// Ensure("test") was called in the constructor — verify both stores are registered.
		await Assert.That(_ftsStores.TryGet(Idx("test"), out _)).IsTrue();
		await Assert.That(_vecStores.TryGet(Idx("test"), out _)).IsTrue();
	}

	[Test]
	public async Task Fixture_Ensure_Is_Idempotent() {
		_ftsStores.TryGet(Idx("test"), out var ftsBefore);
		_vecStores.TryGet(Idx("test"), out var vecBefore);

		Ensure("test"); // second call — should be a no-op

		_ftsStores.TryGet(Idx("test"), out var ftsAfter);
		_vecStores.TryGet(Idx("test"), out var vecAfter);

		// Same instance survives — second call did not replace.
		await Assert.That(ftsAfter).IsSameReferenceAs(ftsBefore!);
		await Assert.That(vecAfter).IsSameReferenceAs(vecBefore!);
	}

	[Test]
	public async Task Fixture_Multiple_Workspaces_Have_Distinct_Stores() {
		Ensure("idx1");
		Ensure("idx2");

		await Assert.That(_ftsStores.TryGet(Idx("idx1"), out _)).IsTrue();
		await Assert.That(_ftsStores.TryGet(Idx("idx2"), out _)).IsTrue();

		_ftsStores.TryGet(Idx("idx1"), out var idx1);
		_ftsStores.TryGet(Idx("idx2"), out var idx2);
		// Distinct stores per workspace.
		await Assert.That(idx1).IsNotSameReferenceAs(idx2!);
	}

	// -------- Hybrid retrieval --------

	[Test]
	public async Task IndexEvent_And_Search_Finds_It() {
		var emb = MakeEmbedding(1);
		Index(1, "order placed widget", emb);

		var candidates = _retriever.HybridSearch(Idx("test"), ["order"], [], emb, SearchAlgorithms.RetrievalCandidates);
		await Assert.That(candidates.Count).IsGreaterThan(0);
	}

	[Test]
	public async Task Search_BM25_Finds_By_Keyword() {
		var emb = MakeEmbedding(42);
		Index(1, "order placed for widgets", emb);
		Index(2, "payment received for invoice", emb);

		var candidates = _retriever.HybridSearch(Idx("test"), ["widgets"], [], emb, SearchAlgorithms.RetrievalCandidates);
		await Assert.That(candidates.Count).IsGreaterThan(0);
		await Assert.That(candidates.All(c => c.Score > 0f)).IsTrue();
	}

	[Test]
	public async Task Search_KNN_Finds_By_Embedding_Similarity() {
		var emb1 = MakeEmbedding(1);
		var emb2 = MakeEmbedding(2);

		Index(1, "order placed", emb1);
		Index(2, "payment received", emb2);

		// Keyword "something" doesn't match either doc's BM25 content — any hit
		// must come from the kNN path. Query with emb1 → doc 1 (which has emb1)
		// should rank above doc 2 by embedding similarity.
		var candidates = _retriever.HybridSearch(Idx("test"), ["something"], [], emb1, SearchAlgorithms.RetrievalCandidates);
		await Assert.That(candidates.Count).IsGreaterThan(0);
		await Assert.That(candidates[0].DocId).IsEqualTo(1ul);
	}

	[Test]
	public async Task Search_Hybrid_Returns_Candidates_With_Normalized_Scores() {
		var emb = MakeEmbedding(1);
		Index(1, "order placed for widgets", emb);

		var candidates = _retriever.HybridSearch(Idx("test"), ["order"], [], emb, SearchAlgorithms.RetrievalCandidates);
		await Assert.That(candidates.Count).IsGreaterThan(0);

		var score = candidates[0].Score;
		await Assert.That(score).IsGreaterThanOrEqualTo(0f);
		await Assert.That(score).IsLessThanOrEqualTo(1f);
	}

	// -------- Browse / list-all --------

	[Test]
	public async Task ListDocs_Returns_All_Indexed_Docs_Without_A_Query() {
		var emb = MakeEmbedding(1);
		Index(1, "order placed", emb);
		Index(2, "payment received", emb);

		var docs = _retriever.ListDocs(Idx("test"), limit: 10);

		await Assert.That(docs.Select(d => d.DocId)).Contains(1ul).And.Contains(2ul);
	}

	[Test]
	public async Task ListDocs_Respects_Limit() {
		var emb = MakeEmbedding(1);
		Index(1, "a", emb);
		Index(2, "b", emb);
		Index(3, "c", emb);

		var docs = _retriever.ListDocs(Idx("test"), limit: 2);

		await Assert.That(docs).Count().IsEqualTo(2);
	}

	[Test]
	public async Task ListDocs_Empty_For_Unknown_Index() {
		var docs = _retriever.ListDocs(Idx("never-created"), limit: 10);
		await Assert.That(docs).IsEmpty();
	}

	[Test]
	public async Task Search_No_Matches_Returns_Empty() {
		var emb = MakeEmbedding(1);
		var candidates = _retriever.HybridSearch(Idx("test"), ["nonexistent"], [], emb, SearchAlgorithms.RetrievalCandidates);
		await Assert.That(candidates).IsEmpty();
	}

	[Test]
	public async Task Search_Respects_Index_Isolation() {
		Ensure("idx_a");
		Ensure("idx_b");

		var emb = MakeEmbedding(1);
		Index(1, "order placed", emb, "idx_a");

		var candidatesA = _retriever.HybridSearch(Idx("idx_a"), ["order"], [], emb, SearchAlgorithms.RetrievalCandidates);
		var candidatesB = _retriever.HybridSearch(Idx("idx_b"), ["order"], [], emb, SearchAlgorithms.RetrievalCandidates);

		await Assert.That(candidatesA.Count).IsGreaterThan(0);
		await Assert.That(candidatesB).IsEmpty();
	}
}
