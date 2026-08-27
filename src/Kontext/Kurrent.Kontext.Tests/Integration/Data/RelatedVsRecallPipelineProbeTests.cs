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
/// PROBE: raw store search versus the FULL recall pipeline, on one corpus.
///
/// Everything measured so far called the store directly. Recall does not — it runs
/// <c>Focused</c>: HybridSearch, then Bm25Reranker, then CognitiveModulator. This asks what that
/// chain costs and, more importantly, whether it returns a DIFFERENT neighbour, since the
/// modulator folds in recency and importance that `related` has no business caring about.
/// </summary>
[Category("Integration")]
[Timeout(300_000)]
public class RelatedVsRecallPipelineProbeTests {
	const int CorpusSize = 500;
	const int Limit      = 5;
	const int Runs       = 10;

	const string Marker = "RELATED-VS-RECALL";

	static readonly DateTimeOffset Now = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask compares_raw_store_search_with_the_recall_pipeline(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var embeddings  = new Pmm12EmbeddingGenerator();

		var options = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };
		var clock   = new FakeClock(Now);

		// The incoming memory, and the one row that near-duplicates it. The duplicate is
		// deliberately OLD and LOW importance — the two dimensions the modulator penalises — so if
		// the pipeline reorders away from pure similarity, this is where it shows.
		var incoming  = "the projector checkpoints only after the batch lands";
		var duplicate = "the projector stores its checkpoint after the batch has landed";

		var texts = new[] { duplicate }
			.Concat(Enumerable.Range(1, CorpusSize - 1).Select(i => $"memory {i} about lance table commits and index maintenance"))
			.ToArray();

		var vectors = await embeddings.GenerateAsync(texts, options, cancellationToken);
		var mine    = KontextMemoryDataStore.EncodeTag(new MemoryContracts.Tag { Scope = "user", Value = "sergio" });

		var rows = texts
			.Select((content, i) => new MemoryRow($"m{i}", MemoryContracts.MemoryType.Fact, content,
					i == 0 ? MemoryContracts.MemoryImportance.Low : MemoryContracts.MemoryImportance.Critical,
					i == 0 ? Now.AddDays(-365) : Now.AddDays(-1)) {
				Embedding      = vectors[i].Vector.ToArray(),
				Tags           = [mine],
				LastAccessedAt = i == 0 ? Now.AddDays(-365) : Now.AddDays(-1),
			})
			.ToArray();

		var store = await MemorySeeding.Seed(dataSources, rows);
		var qv    = (await embeddings.GenerateAsync([incoming], options, cancellationToken))[0].Vector.ToArray();

		MemoryContracts.Tag[] scope = [new() { Scope = "user", Value = "sergio" }];

		var retriever = KontextRetriever.New().Focused(store, embeddings, clock).Build();

		// Act — raw store hybrid, the shape `related` would use.
		var direct = await store
			.SearchAsync(incoming, qv, scope, new HybridSearchOptions { K = Limit }, cancellationToken)
			.ToListAsync(cancellationToken);

		// Act — the full recall pipeline, which also embeds the query itself.
		var ranked = await retriever.RetrieveAsync(
			new RetrievalQuery { Text = incoming, Tags = scope, Limit = Limit, AsOf = Now }, cancellationToken);

		clock.Reset(Now);

		var directTop = direct.Count > 0 ? direct[0].Memory.MemoryId : "<none>";
		var rankedTop = ranked.Count > 0 ? ranked[0].Memory.MemoryId : "<none>";

		// Cost of each, warm.
		for (var i = 0; i < 3; i++) {
			await store.SearchAsync(incoming, qv, scope, new HybridSearchOptions { K = Limit }, cancellationToken).ToListAsync(cancellationToken);
			await retriever.RetrieveAsync(new RetrievalQuery { Text = incoming, Tags = scope, Limit = Limit, AsOf = Now }, cancellationToken);
		}

		var timer = Stopwatch.StartNew();
		for (var i = 0; i < Runs; i++)
			await store.SearchAsync(incoming, qv, scope, new HybridSearchOptions { K = Limit }, cancellationToken).ToListAsync(cancellationToken);
		var directMs = timer.Elapsed.TotalMilliseconds / Runs;

		timer.Restart();
		for (var i = 0; i < Runs; i++)
			await retriever.RetrieveAsync(new RetrievalQuery { Text = incoming, Tags = scope, Limit = Limit, AsOf = Now }, cancellationToken);
		var pipelineMs = timer.Elapsed.TotalMilliseconds / Runs;

		Console.WriteLine($"{Marker} corpus={CorpusSize} limit={Limit} runs={Runs}");
		Console.WriteLine($"{Marker} duplicate-is           m0  (365d old, LOW importance)");
		Console.WriteLine($"{Marker} store-hybrid-top       {directTop}");
		Console.WriteLine($"{Marker} recall-pipeline-top    {rankedTop}");
		Console.WriteLine($"{Marker} store-hybrid-found-dup {direct.Any(h => h.Memory.MemoryId == "m0")}");
		Console.WriteLine($"{Marker} recall-found-dup       {ranked.Any(h => h.Memory.MemoryId == "m0")}");
		Console.WriteLine($"{Marker} store-hybrid           {directMs,7:F1} ms  (embedding already in hand)");
		Console.WriteLine($"{Marker} recall-pipeline        {pipelineMs,7:F1} ms  (embeds internally + 2 stages)");

		await Assert.That(direct).IsNotEmpty();
		await Assert.That(ranked).IsNotEmpty();
	}

	/// <summary>A fixed clock the pipeline can age candidates against without wall-time drift.</summary>
	sealed class FakeClock(DateTimeOffset now) : TimeProvider {
		DateTimeOffset _now = now;

		public void Reset(DateTimeOffset value) => _now = value;

		public override DateTimeOffset GetUtcNow() => _now;
	}
}
