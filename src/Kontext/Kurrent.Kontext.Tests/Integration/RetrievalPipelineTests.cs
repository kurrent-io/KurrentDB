// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Retrieval;

namespace Kurrent.Kontext.Tests;

/// <summary>
/// End-to-end tests for the default retrieval pipeline: a REAL DuckDB + Lance store and the REAL
/// pMM12 embedding model (embedded in <c>KurrentDB.Kontext.Models</c> — no download) behind the
/// exact default composition — vector + keyword search → rank fusion → cognitive modulation → MMR.
/// Memories are seeded with embeddings the model produced from their content, then recalled by
/// query TEXT, so the embed → search → fuse → rank path runs live.
///
/// The seams other suites own stay out: the store's raw SearchAsync (KontextDataStoreTests), the
/// service mapping (KontextMemoryTests), each stage's math (Kurrent.Kontext.Retrieval.Tests).
///
/// Seeds are crafted for stable ranking: uniform type/importance/recency keeps modulation neutral,
/// disjoint vocabularies keep MMR's Jaccard similarity at zero, and query words never appear in
/// any content except where a test says so — relevance alone decides.
/// </summary>
[Category("Integration")]
public class RetrievalPipelineTests {
	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	[ClassDataSource<KontextStoreFixture>(Shared = SharedType.None)]
	public required KontextStoreFixture Fixture { get; init; }

	[Test]
	public async ValueTask recalls_by_meaning_without_shared_keywords() {
		// Arrange
		var expected = Memory("eagle", "Eagles soar over mountain ridges hunting for prey");

		await Fixture.SeedEmbedded(
			expected,
			Memory("invoice", "The quarterly invoice ledger was reconciled by accounting"),
			Memory("bread", "Sourdough loaves rise slowly during cold fermentation"));

		var retriever = DefaultPipeline(Fixture);

		// Act
		var result = await retriever.RetrieveAsync(new RetrievalQuery {
			Text = "birds flying across sunny skies",
			AsOf = Base
		});

		// Assert
		await Assert.That(result.Count).IsEqualTo(3);
		await Assert.That(result[0].Memory.MemoryId).IsEqualTo(expected.Id);
	}

	[Test]
	public async ValueTask fuses_the_vector_and_keyword_legs_into_one_ranked_pool() {
		// Arrange
		var penguin = Memory("penguin", "Emperor penguins huddle together through polar nights");
		var ticket  = Memory("ticket", "KDB-1234 tracks that flaky checkpoint test on our build server");

		await Fixture.SeedEmbedded(
			penguin,
			ticket,
			Memory("bakery", "Bakeries order fresh flour every monday morning"));

		var retriever = DefaultPipeline(Fixture);

		// Act
		var result = await retriever.RetrieveAsync(new() {
			Text = "KDB-1234 how do birds stay warm when freezing",
			AsOf = Base
		});

		// Assert
		var topTwo = result.Take(2).Select(scored => scored.Memory.MemoryId).ToList();

		await Assert.That(topTwo).IsEquivalentTo([penguin.Id, ticket.Id]);

		var penguinHit = result.Single(scored => scored.Memory.MemoryId == penguin.Id);
		var ticketHit  = result.Single(scored => scored.Memory.MemoryId == ticket.Id);

		await Assert.That(penguinHit.Breakdown.SourceRanks[RetrievalSources.Vector]).IsEqualTo(1);
		await Assert.That(ticketHit.Breakdown.SourceRanks[RetrievalSources.Keyword]).IsEqualTo(1);
	}

	#region ->> Test Infrastructure <<-

	static IKontextRetriever DefaultPipeline(KontextStoreFixture fixture) =>
		KontextRetriever.New().Default(fixture.Store, fixture.EmbeddingGenerator).Build();

	/// <summary>Uniform metadata so modulation stays neutral — relevance alone decides the ranking.</summary>
	static MemoryRow Memory(string id, string content) =>
		new(
			Id: id,
			Type: Contracts.MemoryType.Fact,
			Content: content,
			Importance: Contracts.MemoryImportance.Normal,
			RetainedAt: Base);

	#endregion // Test Infrastructure
}
