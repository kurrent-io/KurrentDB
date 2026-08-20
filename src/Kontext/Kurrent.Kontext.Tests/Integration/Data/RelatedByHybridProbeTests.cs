// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Memory.Data;
using Kurrent.Kontext.Retrieval;
using Kurrent.Kontext.Testing;
using Microsoft.Extensions.AI;
using TUnit.Assertions.Enums;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// PROBE: the same three questions <see cref="RelatedByFullTextProbeTests"/> asks, put to the
/// store's HYBRID SearchAsync — the overload that costs retain an embedding generator.
///
/// The third test is the whole point of running both probes: full-text provably MISSES a reworded
/// duplicate that shares no token. Does paying for the vector leg buy that case back? If it does
/// not, the embedding dependency is unjustified for `related` and full-text is simply the answer.
/// </summary>
[Category("Integration")]
[Timeout(120_000)]
public class RelatedByHybridProbeTests {
	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask hybrid_surfaces_the_near_duplicate_first(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var embeddings  = new Pmm12EmbeddingGenerator();

		var incoming = "tests run only through scripts/testing/test-runner.cs";

		var store = await SeedEmbedded(dataSources, embeddings, cancellationToken,
			Row("near-duplicate", "the test runner lives at scripts/testing/test-runner.cs"),
			Row("unrelated-1",    "penguins waddle across antarctic ice"),
			Row("unrelated-2",    "the projector checkpoints after the batch lands"),
			Row("unrelated-3",    "giraffes browse the tallest acacia leaves"));

		var query = await Embed(embeddings, incoming, cancellationToken);

		// Act — the call retain would make once it holds an embedding generator.
		var hits = await store
			.SearchAsync(incoming, query, [], new HybridSearchOptions { K = 3 }, cancellationToken)
			.ToListAsync(cancellationToken);

		// Assert — the blend score is what `related.similarity` would report.
		await Assert.That(hits).IsNotEmpty();
		await Assert.That(hits[0].Memory.MemoryId).IsEqualTo("near-duplicate");
		await Assert.That(hits[0].HybridScore).IsNotNull();
		await Assert.That(hits[0].HybridScore!.Value).IsGreaterThan(0);
	}

	[Test]
	public async ValueTask hybrid_never_returns_a_superseded_memory(CancellationToken cancellationToken) {
		// Arrange — the superseded row is the best match on BOTH legs, so if it can leak, it will.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var embeddings  = new Pmm12EmbeddingGenerator();

		var incoming = "the test runner lives at scripts/testing/test-runner.cs";

		var store = await SeedEmbedded(dataSources, embeddings, cancellationToken,
			Row("superseded", "the test runner lives at scripts/testing/test-runner.cs") with {
				IsSuperseded = true,
				SupersededAt = Base.AddHours(1),
				SupersededBy = "live",
			},
			Row("live", "the test runner moved to tools/test-runner.cs"));

		var query           = await Embed(embeddings, incoming, cancellationToken);
		var expectedVisible = new List<string> { "live" };

		// Act
		var hits = await store
			.SearchAsync(incoming, query, [], new HybridSearchOptions { K = 10 }, cancellationToken)
			.ToListAsync(cancellationToken);

		// Assert
		var ids = hits.Select(hit => hit.Memory.MemoryId).ToList();

		await Assert.That(ids).IsEquivalentTo(expectedVisible, CollectionOrdering.Any);
	}

	[Test]
	public async ValueTask hybrid_finds_the_reworded_duplicate_full_text_misses(CancellationToken cancellationToken) {
		// Arrange — the exact corpus and query that full-text provably misses. Same words, same
		// rows, only the search mode changes, so the delta is attributable to the vector leg alone.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var embeddings  = new Pmm12EmbeddingGenerator();

		var incoming = "a cat sat on a mat";

		var store = await SeedEmbedded(dataSources, embeddings, cancellationToken,
			Row("semantic-duplicate", "the feline rested upon the rug"),
			Row("lexical-noise",      "deployment pipelines were migrated between regions"));

		var query = await Embed(embeddings, incoming, cancellationToken);

		// Act
		var hits = await store
			.SearchAsync(incoming, query, [], new HybridSearchOptions { K = 5 }, cancellationToken)
			.ToListAsync(cancellationToken);

		// Assert — this is what the embedding dependency buys, stated as a measurement.
		var found = hits.Any(hit => hit.Memory.MemoryId == "semantic-duplicate");

		await Assert.That(found).IsTrue();
	}

	#region ->> Probe Infrastructure <<-

	static async ValueTask<float[]> Embed(Pmm12EmbeddingGenerator embeddings, string text, CancellationToken ct) {
		var options    = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };
		var generated  = await embeddings.GenerateAsync([text], options, ct);

		return generated[0].Vector.ToArray();
	}

	static MemoryRow Row(string id, string content) =>
		new(id, Contracts.MemoryType.Fact, content, Contracts.MemoryImportance.Normal, Base);

	// Replaces each row's placeholder embedding with the REAL vector for its content — the seeding
	// default is a fixed stub, which would make the vector leg meaningless here.
	static async ValueTask<KontextMemoryDataStore> SeedEmbedded(
		KontextDataSource dataSource,
		Pmm12EmbeddingGenerator embeddings,
		CancellationToken ct,
		params MemoryRow[] rows
	) {
		var options = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };
		var vectors = await embeddings.GenerateAsync(rows.Select(row => row.Content).ToArray(), options, ct);

		var embedded = rows
			.Select((row, i) => row with { Embedding = vectors[i].Vector.ToArray() })
			.ToArray();

		return await MemorySeeding.Seed(dataSource, embedded);
	}

	#endregion // Probe Infrastructure
}
