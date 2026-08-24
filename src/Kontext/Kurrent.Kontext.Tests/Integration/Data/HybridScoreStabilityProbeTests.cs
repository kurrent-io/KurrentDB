// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Memory.Data;
using Kurrent.Kontext.Retrieval;
using Kurrent.Kontext.Testing;
using Microsoft.Extensions.AI;
using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// PROBE: is _hybrid_score STABLE for a given pair, or does it move with the request?
///
/// The store derives the candidate pool as k = Math.Max(K, Limit), and the engine blends the two
/// legs across that pool. If the blend normalises over the pool, then asking for more results
/// changes the score of a pair that did not change — which would make any fixed threshold
/// meaningless even for one corpus, never mind across queries.
///
/// One query, one corpus, one twin. Only Limit and K move.
/// </summary>
[Category("Integration")]
[Timeout(300_000)]
public class HybridScoreStabilityProbeTests {
	const int NoiseSize = 200;

	const string Marker = "SCORE-STABILITY";

	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask hybrid_score_moves_with_limit_and_k(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var embeddings  = new Pmm12EmbeddingGenerator();

		var options   = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };
		var twinText  = "the test runner lives at scripts/testing/test-runner.cs";
		var incoming  = "tests run only through scripts/testing/test-runner.cs";

		var texts = new[] { twinText }
			.Concat(Enumerable.Range(0, NoiseSize).Select(i => $"note {i} about lance commits and checkpoint bookkeeping"))
			.ToArray();

		var vectors = await embeddings.GenerateAsync(texts, options, cancellationToken);
		var tag     = KontextMemoryDataStore.EncodeTag(new MemoryContracts.Tag { Scope = "user", Value = "sergio" });

		var rows = texts
			.Select((content, i) => new MemoryRow($"m{i}", MemoryContracts.MemoryType.Fact, content, MemoryContracts.MemoryImportance.Normal, Base) {
				Embedding = vectors[i].Vector.ToArray(),
				Tags      = [tag],
			})
			.ToArray();

		var store = await MemorySeeding.Seed(dataSources, rows);
		var qv    = (await embeddings.GenerateAsync([incoming], options, cancellationToken))[0].Vector.ToArray();

		MemoryContracts.Tag[] scope = [new() { Scope = "user", Value = "sergio" }];

		Console.WriteLine($"{Marker} one twin, {NoiseSize} noise rows, identical query every time");
		Console.WriteLine($"{Marker}  limit    K   effK   twinScore  bestNoise");

		// Limit sweep at the default K — k = Max(K, Limit), so Limit drives the pool.
		foreach (var limit in (int[])[1, 3, 5, 10, 25, 50, 100]) {
			var opts = new HybridSearchOptions { K = limit };
			var hits = await store.SearchAsync(incoming, qv, scope, opts, cancellationToken).ToListAsync(cancellationToken);

			var twin      = hits.FirstOrDefault(h => h.Memory.Content == twinText);
			var twinScore = twin.Memory is null ? double.NaN : twin.HybridScore!.Value;
			var bestNoise = hits.Where(h => h.Memory.Content != twinText).Select(h => h.HybridScore!.Value).DefaultIfEmpty(double.NaN).Max();

			Console.WriteLine($"{Marker} {limit,6} {opts.K,4} {Math.Max(opts.K, limit),6}   {twinScore,9:F4}  {bestNoise,9:F4}");
		}

		// K sweep at a fixed page — same page size, bigger pool.
		Console.WriteLine($"{Marker} --- fixed limit=5, K swept ---");

		foreach (var k in (int[])[5, 10, 25, 50, 100, 200]) {
			var opts = new HybridSearchOptions { K = k };
			var hits = await store.SearchAsync(incoming, qv, scope, opts, cancellationToken).ToListAsync(cancellationToken);

			var twin      = hits.FirstOrDefault(h => h.Memory.Content == twinText);
			var twinScore = twin.Memory is null ? double.NaN : twin.HybridScore!.Value;
			var bestNoise = hits.Where(h => h.Memory.Content != twinText).Select(h => h.HybridScore!.Value).DefaultIfEmpty(double.NaN).Max();

			Console.WriteLine($"{Marker} {5,6} {k,4} {Math.Max(k, 5),6}   {twinScore,9:F4}  {bestNoise,9:F4}");
		}

		// Alpha sweep at a fixed page, to show the blend weight moving the same pair.
		Console.WriteLine($"{Marker} --- fixed limit=5 K=10, alpha swept ---");

		foreach (var alpha in (double[])[0.0, 0.25, 0.5, 0.75, 1.0]) {
			var opts = new HybridSearchOptions { K = 5, Alpha = alpha };
			var hits = await store.SearchAsync(incoming, qv, scope, opts, cancellationToken).ToListAsync(cancellationToken);

			var twin      = hits.FirstOrDefault(h => h.Memory.Content == twinText);
			var twinScore = twin.Memory is null ? double.NaN : twin.HybridScore!.Value;

			Console.WriteLine($"{Marker} alpha={alpha,4:F2}              {twinScore,9:F4}");
		}

		await Assert.That(rows.Length).IsEqualTo(NoiseSize + 1);
	}
}
