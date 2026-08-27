// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Memory.Data;
using Kurrent.Kontext.Retrieval;
using Kurrent.Kontext.Testing;
using Microsoft.Extensions.AI;
using MemoryContracts = Kurrent.Kontext.Contracts.Memory;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// PROBE: tunes a pipeline for `related` — finding the memory a caller is ABOUT TO DUPLICATE.
///
/// That is a different objective from recall. Recall answers "what is relevant to this question";
/// `related` answers "does this already exist". So the Focused chain's pinned alpha (0.45, measured
/// for recall) and its Bm25Reranker are assumptions here, not settings.
///
/// Method: a noise corpus with planted duplicate pairs of two kinds — LEXICAL restatements that
/// share most words, and SEMANTIC rewordings that share almost none. Each probe query is a memory
/// about to be retained; success is its planted twin ranking high. Scored by MRR, split by kind,
/// because the two kinds pull alpha in opposite directions and an average would hide that.
/// </summary>
[Category("Integration")]
[Timeout(600_000)]
public class RelatedPipelineTuningProbeTests {
	const int NoiseSize = 300;
	const int Limit     = 10;

	const string Marker = "RELATED-TUNE";

	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	// (stored twin, incoming memory). The stored one is what `related` must surface.
	static readonly (string Stored, string Incoming)[] Lexical = [
		("the test runner lives at scripts/testing/test-runner.cs",        "tests run only through scripts/testing/test-runner.cs"),
		("the projector checkpoints after the batch lands",                "the projector checkpoints once the batch has landed"),
		("KontextMemoryWriter batches every statement into one command",   "KontextMemoryWriter puts every statement in a single command"),
		("the memories table stores log_position with a BTREE index",      "log_position on the memories table carries a BTREE index"),
		("recall embeds content and nothing else",                         "only content is embedded by recall"),
		("retain mints every memory id on the server",                     "the server mints each memory id during retain"),
	];

	static readonly (string Stored, string Incoming)[] Semantic = [
		("the feline rested upon the rug",                                 "a cat sat on a mat"),
		("the build broke after the dependency bump",                      "CI went red once the package version changed"),
		("we abandoned the second index because it cost too much disk",    "the extra lookup structure was dropped over storage overhead"),
		("the writer never mutates rows the projector owns",               "only the projection process changes those records"),
		("a colleague reported the outage during standup",                 "someone mentioned the downtime at the morning meeting"),
		("the schema was reset rather than migrated",                      "instead of upgrading, the tables were rebuilt from scratch"),
	];

	[Test]
	public async ValueTask sweeps_alpha_and_the_reranker_for_duplicate_detection(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var embeddings  = new Pmm12EmbeddingGenerator();

		var options = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };
		var pairs   = Lexical.Select(p => (p.Stored, p.Incoming, Kind: "lexical"))
			.Concat(Semantic.Select(p => (p.Stored, p.Incoming, Kind: "semantic")))
			.ToArray();

		// The stored twins come first so their ids are predictable, then the noise.
		var stored = pairs.Select(pair => pair.Stored)
			.Concat(Enumerable.Range(0, NoiseSize).Select(i => $"note {i} about lance commits, index maintenance and checkpoint bookkeeping"))
			.ToArray();

		var vectors = await embeddings.GenerateAsync(stored, options, cancellationToken);
		var tag     = KontextMemoryDataStore.EncodeTag(new MemoryContracts.Tag { Scope = "user", Value = "sergio" });

		var rows = stored
			.Select((content, i) => new MemoryRow($"m{i}", MemoryContracts.MemoryType.Fact, content, MemoryContracts.MemoryImportance.Normal, Base) {
				Embedding = vectors[i].Vector.ToArray(),
				Tags      = [tag],
			})
			.ToArray();

		var store = await MemorySeeding.Seed(dataSources, rows);

		MemoryContracts.Tag[] scope = [new() { Scope = "user", Value = "sergio" }];

		Console.WriteLine($"{Marker} noise={NoiseSize} pairs={pairs.Length} limit={Limit}  (MRR, higher is better)");
		Console.WriteLine($"{Marker} alpha  reranker   lexical  semantic   overall");

		foreach (var alpha in (double[])[0.0, 0.25, 0.45, 0.65, 0.85, 1.0]) {
			foreach (var reranked in (bool[])[false, true]) {
				var builder = KontextRetriever.New()
					.Planner(new OverfetchOptions())
					.AddSearch(new HybridSearch(store, embeddings, alpha));

				if (reranked)
					builder = builder.AddStage(Bm25Reranker.Create());

				var retriever = builder.Build();

				var lexicalMrr  = await Mrr(retriever, pairs.Where(p => p.Kind == "lexical"), scope, cancellationToken);
				var semanticMrr = await Mrr(retriever, pairs.Where(p => p.Kind == "semantic"), scope, cancellationToken);
				var overallMrr  = (lexicalMrr + semanticMrr) / 2;

				Console.WriteLine($"{Marker} {alpha,4:F2}  {(reranked ? "bm25    " : "none    ")}   {lexicalMrr,7:F3}  {semanticMrr,8:F3}  {overallMrr,8:F3}");
			}
		}

		await Assert.That(pairs.Length).IsEqualTo(Lexical.Length + Semantic.Length);
	}

	// Mean reciprocal rank of each pair's planted twin. A twin outside the page scores 0.
	static async ValueTask<double> Mrr(
		IKontextRetriever retriever,
		IEnumerable<(string Stored, string Incoming, string Kind)> pairs,
		MemoryContracts.Tag[] scope,
		CancellationToken ct
	) {
		var total = 0.0;
		var count = 0;

		foreach (var pair in pairs) {
			var ranked = await retriever.RetrieveAsync(
				new RetrievalQuery { Text = pair.Incoming, Tags = scope, Limit = Limit, AsOf = Base }, ct);

			var rank = ranked
				.Select((scored, i) => (Content: scored.Memory.Content, Rank: i + 1))
				.FirstOrDefault(entry => entry.Content == pair.Stored);

			total += rank.Rank > 0 ? 1.0 / rank.Rank : 0.0;
			count++;
		}

		return count == 0 ? 0 : total / count;
	}
}
