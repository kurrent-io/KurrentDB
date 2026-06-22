// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Kontext.Tests.Search;

public class SearchAlgorithmsMmrTests {
	static HydratedDoc MakeDoc(string label, float[]? embedding = null) => new() {
		Stream = "test-stream",
		EventNumber = 0,
		EventType = label,
		Timestamp = default,
		Data = default,
		IsJson = false,
		Metadata = default,
		Embedding = embedding,
	};

	[Test]
	public async Task MmrSelect_Prefers_Diverse_Embeddings() {
		// Two similar docs and one different
		var similar1 = new float[] { 1, 0, 0 };
		var similar2 = new float[] { 0.99f, 0.1f, 0 };
		var different = new float[] { 0, 0, 1 };

		var docs = new Dictionary<ulong, HydratedDoc> {
			[1] = MakeDoc("sim1", similar1),
			[2] = MakeDoc("sim2", similar2),
			[3] = MakeDoc("diff", different),
		};

		var candidates = new List<ScoredDoc>
		{
			new(1, 1.0f),
			new(2, 0.95f),
			new(3, 0.9f),
		};

		// topK < candidates.Count so MMR actually selects (otherwise it short-circuits
		// and returns the input as-is).
		var selected = SearchAlgorithms.MmrSelect(candidates, docs, 2);

		// sim1 wins first slot on pure relevance.
		await Assert.That(selected[0].DocId).IsEqualTo(1ul);
		// diff beats sim2 for the second slot despite lower relevance because it's far
		// from sim1 (diversity term dominates).
		await Assert.That(selected[1].DocId).IsEqualTo(3ul);
	}

	[Test]
	public async Task MmrSelect_Fewer_Candidates_Than_TopK() {
		var docs = new Dictionary<ulong, HydratedDoc> {
			[1] = MakeDoc("doc1", [1, 0]),
			[2] = MakeDoc("doc2", [0, 1]),
		};

		var candidates = new List<ScoredDoc>
		{
			new(1, 1.0f),
			new(2, 0.8f),
		};

		var selected = SearchAlgorithms.MmrSelect(candidates, docs, 10);
		await Assert.That(selected).Count().IsEqualTo(2);
	}

	[Test]
	public async Task MmrSelect_Empty_Candidates() {
		var docs = new Dictionary<ulong, HydratedDoc>();
		var candidates = new List<ScoredDoc>();

		var selected = SearchAlgorithms.MmrSelect(candidates, docs, 10);
		await Assert.That(selected).IsEmpty();
	}

	[Test]
	public async Task MmrSelect_No_Embedding_Falls_Back_To_Relevance() {
		var docs = new Dictionary<ulong, HydratedDoc> {
			[1] = MakeDoc("doc1"),
			[2] = MakeDoc("doc2"),
		};

		var candidates = new List<ScoredDoc>
		{
			new(1, 1.0f),
			new(2, 0.5f),
		};

		var selected = SearchAlgorithms.MmrSelect(candidates, docs, 2);
		await Assert.That(selected[0].DocId).IsEqualTo(1ul);
		await Assert.That(selected[1].DocId).IsEqualTo(2ul);
	}

	[Test]
	public async Task Similarity_Token_Jaccard_When_No_Embedding() {
		var a = new HydratedDoc {
			Stream = "s", EventNumber = 0, EventType = "t", Timestamp = default, Data = default, IsJson = false, Metadata = default,
			Tokens = ["a", "b", "c"]
		};
		var b = new HydratedDoc {
			Stream = "s", EventNumber = 0, EventType = "t", Timestamp = default, Data = default, IsJson = false, Metadata = default,
			Tokens = ["b", "c", "d"]
		};

		var sim = SearchAlgorithms.Similarity(a, b);
		// |{b,c}| / |{a,b,c,d}| = 2/4 = 0.5
		await Assert.That(sim).IsEqualTo(0.5f);
	}
}