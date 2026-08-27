// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
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
/// PROBE: the three questions left open for `related`.
///
/// 1. Is a bare pipeline (Planner + HybridSearch) any different from calling the store's hybrid
///    SearchAsync directly? If not, `related` needs no pipeline at all.
/// 2. Where does the missing semantic recall go — are the twins absent, or present but low?
/// 3. Can a similarity FLOOR separate true duplicates from noise, so `related` returns the
///    near-duplicates or nothing instead of five arbitrary rows on every retain?
/// </summary>
[Category("Integration")]
[Timeout(600_000)]
public class RelatedFloorAndPipelineProbeTests {
	const int NoiseSize = 300;
	const int Limit     = 10;
	const int Runs      = 10;

	const string Marker = "RELATED-FLOOR";

	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	static readonly (string Stored, string Incoming, string Kind)[] Pairs = [
		("the test runner lives at scripts/testing/test-runner.cs",      "tests run only through scripts/testing/test-runner.cs",      "lexical"),
		("the projector checkpoints after the batch lands",              "the projector checkpoints once the batch has landed",        "lexical"),
		("KontextMemoryWriter batches every statement into one command", "KontextMemoryWriter puts every statement in a single command","lexical"),
		("the memories table stores log_position with a BTREE index",    "log_position on the memories table carries a BTREE index",   "lexical"),
		("recall embeds content and nothing else",                       "only content is embedded by recall",                         "lexical"),
		("retain mints every memory id on the server",                   "the server mints each memory id during retain",              "lexical"),
		("the feline rested upon the rug",                               "a cat sat on a mat",                                         "semantic"),
		("the build broke after the dependency bump",                    "CI went red once the package version changed",               "semantic"),
		("we abandoned the second index because it cost too much disk",  "the extra lookup structure was dropped over storage overhead","semantic"),
		("the writer never mutates rows the projector owns",             "only the projection process changes those records",          "semantic"),
		("a colleague reported the outage during standup",               "someone mentioned the downtime at the morning meeting",      "semantic"),
		("the schema was reset rather than migrated",                    "instead of upgrading, the tables were rebuilt from scratch", "semantic"),
	];

	[Test]
	public async ValueTask compares_bare_pipeline_to_direct_search_and_measures_a_floor(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var embeddings  = new Pmm12EmbeddingGenerator();

		var options = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };

		var stored = Pairs.Select(pair => pair.Stored)
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

		MemoryContracts.Tag[] scope     = [new() { Scope = "user", Value = "sergio" }];
		var             retriever = KontextRetriever.New()
			.Planner(new OverfetchOptions())
			.AddSearch(new HybridSearch(store, embeddings, 0.45))
			.Build();

		// ---- 1. bare pipeline vs direct store call -------------------------------------------
		var identical = 0;

		foreach (var pair in Pairs) {
			var qv = (await embeddings.GenerateAsync([pair.Incoming], options, cancellationToken))[0].Vector.ToArray();

			var direct = await store
				.SearchAsync(pair.Incoming, qv, scope, new HybridSearchOptions { K = Limit }, cancellationToken)
				.ToListAsync(cancellationToken);

			var piped = await retriever.RetrieveAsync(
				new RetrievalQuery { Text = pair.Incoming, Tags = scope, Limit = Limit, AsOf = Base }, cancellationToken);

			if (direct.Select(h => h.Memory.MemoryId).SequenceEqual(piped.Select(s => s.Memory.MemoryId)))
				identical++;
		}

		// ---- 2 + 3. per-pair rank, twin score, and best NON-twin score ------------------------
		Console.WriteLine($"{Marker} noise={NoiseSize} limit={Limit} alpha=0.45");
		Console.WriteLine($"{Marker} identical-orderings   {identical}/{Pairs.Length}   (bare pipeline vs direct store call)");
		Console.WriteLine($"{Marker} kind      rank  twinScore  bestNoise   margin");

		var twinScores  = new List<double>();
		var noiseScores = new List<double>();

		foreach (var pair in Pairs) {
			var qv = (await embeddings.GenerateAsync([pair.Incoming], options, cancellationToken))[0].Vector.ToArray();

			var hits = await store
				.SearchAsync(pair.Incoming, qv, scope, new HybridSearchOptions { K = 50 }, cancellationToken)
				.ToListAsync(cancellationToken);

			var twinIndex = hits.FindIndex(h => h.Memory.Content == pair.Stored);
			var twinScore = twinIndex >= 0 ? hits[twinIndex].HybridScore!.Value : double.NaN;
			var bestNoise = hits.Where(h => h.Memory.Content != pair.Stored).Select(h => h.HybridScore!.Value).DefaultIfEmpty(double.NaN).Max();

			if (twinIndex >= 0) twinScores.Add(twinScore);
			if (!double.IsNaN(bestNoise)) noiseScores.Add(bestNoise);

			Console.WriteLine($"{Marker} {pair.Kind,-9} {(twinIndex >= 0 ? (twinIndex + 1).ToString() : "MISS"),4}  {twinScore,9:F4}  {bestNoise,9:F4}  {twinScore - bestNoise,7:F4}");
		}

		Console.WriteLine($"{Marker} twin-score   min={twinScores.Min(),7:F4} max={twinScores.Max(),7:F4} mean={twinScores.Average(),7:F4}");
		Console.WriteLine($"{Marker} noise-score  min={noiseScores.Min(),7:F4} max={noiseScores.Max(),7:F4} mean={noiseScores.Average(),7:F4}");

		// A floor is only viable if the worst twin outscores the best noise.
		Console.WriteLine($"{Marker} floor-viable {(twinScores.Min() > noiseScores.Max() ? "YES" : "NO")}   worstTwin={twinScores.Min():F4} bestNoise={noiseScores.Max():F4}");

		// ---- cost of each shape --------------------------------------------------------------
		var probe = Pairs[0].Incoming;
		var pv    = (await embeddings.GenerateAsync([probe], options, cancellationToken))[0].Vector.ToArray();

		for (var i = 0; i < 3; i++) {
			await store.SearchAsync(probe, pv, scope, new HybridSearchOptions { K = Limit }, cancellationToken).ToListAsync(cancellationToken);
			await retriever.RetrieveAsync(new RetrievalQuery { Text = probe, Tags = scope, Limit = Limit, AsOf = Base }, cancellationToken);
		}

		var clock = Stopwatch.StartNew();
		for (var i = 0; i < Runs; i++)
			await store.SearchAsync(probe, pv, scope, new HybridSearchOptions { K = Limit }, cancellationToken).ToListAsync(cancellationToken);
		var directMs = clock.Elapsed.TotalMilliseconds / Runs;

		clock.Restart();
		for (var i = 0; i < Runs; i++)
			await retriever.RetrieveAsync(new RetrievalQuery { Text = probe, Tags = scope, Limit = Limit, AsOf = Base }, cancellationToken);
		var pipedMs = clock.Elapsed.TotalMilliseconds / Runs;

		Console.WriteLine($"{Marker} direct-store   {directMs,7:F1} ms  (embedding in hand)");
		Console.WriteLine($"{Marker} bare-pipeline  {pipedMs,7:F1} ms  (embeds internally)");

		await Assert.That(twinScores).IsNotEmpty();
	}
}
