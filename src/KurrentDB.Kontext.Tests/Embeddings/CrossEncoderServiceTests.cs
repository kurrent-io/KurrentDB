// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Kontext.Tests.Embeddings;

public class CrossEncoderServiceTests {
	static CrossEncoderService? _service;

	[Before(Class)]
	public static async Task Setup(ClassHookContext context) {
		var modelManager = new ModelManager(NullLogger<ModelManager>.Instance);
		_service = new CrossEncoderService(modelManager,
			NullLogger<CrossEncoderService>.Instance);
		await _service.InitializeAsync();
	}

	[After(Class)]
	public static async Task Teardown(ClassHookContext context) {
		_service?.Dispose();
		await Task.CompletedTask;
	}

	static async Task<List<ScoredDoc>> ToListAsync(IAsyncEnumerable<ScoredDoc> src) {
		var list = new List<ScoredDoc>();
		await foreach (var r in src)
			list.Add(r);
		return list;
	}

	[Test]
	public async Task Rerank_Returns_All_Documents() {
		var docs = new List<(ulong Id, string Document)>
		{
			(1, "Order was placed successfully"),
			(2, "The weather is nice today"),
			(3, "Customer placed a new order"),
		};

		var ranked = await ToListAsync(_service!.RerankAsync("What orders were placed?", docs));

		await Assert.That(ranked.Count).IsEqualTo(3);
	}

	[Test]
	public async Task Rerank_Relevant_Document_Scores_Higher() {
		var docs = new List<(ulong Id, string Document)>
		{
			(1, "The customer placed an order for widgets"),
			(2, "The sun rises in the east every morning"),
		};

		var ranked = await ToListAsync(_service!.RerankAsync("What orders were placed?", docs));

		var relevantScore = ranked.First(x => x.DocId == 1).Score;
		var irrelevantScore = ranked.First(x => x.DocId == 2).Score;
		await Assert.That(relevantScore).IsGreaterThan(irrelevantScore);
	}

	[Test]
	public async Task Rerank_Results_Sorted_By_Score_Descending() {
		var docs = new List<(ulong Id, string Document)>
		{
			(1, "Order placed"),
			(2, "Sunny weather"),
			(3, "New order submitted"),
		};

		var ranked = await ToListAsync(_service!.RerankAsync("order", docs));

		for (var i = 1; i < ranked.Count; i++)
			await Assert.That(ranked[i - 1].Score).IsGreaterThanOrEqualTo(ranked[i].Score);
	}

	[Test]
	public async Task Rerank_Includes_Empty_Passages_In_Results() {
		var docs = new List<(ulong Id, string Document)>
		{
			(1, ""),
			(2, "Order placed for a widget"),
		};

		var ranked = await ToListAsync(_service!.RerankAsync("order", docs));
		await Assert.That(ranked.Count).IsEqualTo(2);
		await Assert.That(ranked.Any(d => d.DocId == 1)).IsTrue();
	}

	[Test]
	public async Task Rerank_Not_Initialized_Throws() {
		var modelManager = new ModelManager(NullLogger<ModelManager>.Instance);
		var uninitService = new CrossEncoderService(modelManager,
			NullLogger<CrossEncoderService>.Instance);

		var docs = new List<(ulong Id, string Document)>
		{
			(1, "test"),
		};

		await Assert.That(async () => await ToListAsync(uninitService.RerankAsync("test", docs)))
			.Throws<InvalidOperationException>();
	}
}