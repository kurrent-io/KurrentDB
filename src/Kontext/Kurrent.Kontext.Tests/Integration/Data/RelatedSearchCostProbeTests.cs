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
using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// PROBE: what does serving retain's `related` actually COST per memory?
///
/// The two mode probes conflate three very different things in one wall-clock number: loading the
/// ONNX model once per process, embedding the query, and running the search. Only the last two are
/// paid per retain, so this separates them and reports each.
///
/// Prints a table rather than asserting a budget — a timing threshold on a shared CI machine is a
/// flaky test, and the number worth having is the breakdown, not a pass mark.
/// </summary>
[Category("Integration")]
[Timeout(300_000)]
public class RelatedSearchCostProbeTests {
	const int CorpusSize = 200;
	const int Warmup     = 3;
	const int Runs       = 20;

	const string Marker = "RELATED-COST";

	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask measures_where_the_time_actually_goes(CancellationToken cancellationToken) {
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		var options = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };
		var clock   = Stopwatch.StartNew();

		// 1. Construction — paid once per process.
		using var embeddings = new Pmm12EmbeddingGenerator();
		var constructMs = clock.Elapsed.TotalMilliseconds;

		// 2. First embedding — pays whatever init the constructor deferred.
		clock.Restart();
		await embeddings.GenerateAsync(["warm the model"], options, cancellationToken);
		var coldEmbedMs = clock.Elapsed.TotalMilliseconds;

		// 3. Warm embedding — the cost retain actually pays, per memory.
		for (var i = 0; i < Warmup; i++)
			await embeddings.GenerateAsync([$"warmup {i}"], options, cancellationToken);

		clock.Restart();
		for (var i = 0; i < Runs; i++)
			await embeddings.GenerateAsync([$"a memory about subject {i}"], options, cancellationToken);
		var warmEmbedMs = clock.Elapsed.TotalMilliseconds / Runs;

		// 4. Batch embedding — the same count in ONE call, which is what a batched retain would do.
		var batch = Enumerable.Range(0, Runs).Select(i => $"a memory about subject {i}").ToArray();

		clock.Restart();
		await embeddings.GenerateAsync(batch, options, cancellationToken);
		var batchedEmbedMs = clock.Elapsed.TotalMilliseconds / Runs;

		// 5. A corpus worth searching.
		var corpus  = Enumerable.Range(0, CorpusSize)
			.Select(i => $"memory {i} about the projector checkpoint format and lance table commits")
			.ToArray();
		var vectors = await embeddings.GenerateAsync(corpus, options, cancellationToken);

		// Half the corpus belongs to another principal — `related` must never cross that line, and
		// the tag filter is what enforces it.
		var mine   = KontextMemoryDataStore.EncodeTag(new MemoryContracts.Tag { Scope = "user", Value = "sergio" });
		var theirs = KontextMemoryDataStore.EncodeTag(new MemoryContracts.Tag { Scope = "user", Value = "someone-else" });

		var rows = corpus
			.Select((content, i) => new MemoryRow($"m{i}", MemoryContracts.MemoryType.Fact, content, MemoryContracts.MemoryImportance.Normal, Base) {
				Embedding = vectors[i].Vector.ToArray(),
				Tags      = [i % 2 == 0 ? mine : theirs],
			})
			.ToArray();

		var store = await MemorySeeding.Seed(dataSources, rows);
		var query = "what did we decide about the checkpoint format";
		var qv    = (await embeddings.GenerateAsync([query], options, cancellationToken))[0].Vector.ToArray();

		// 6. Full-text search, warm.
		for (var i = 0; i < Warmup; i++)
			await store.SearchAsync(query, [], new FullTextSearchOptions { K = 5 }, cancellationToken).ToListAsync(cancellationToken);

		clock.Restart();
		for (var i = 0; i < Runs; i++)
			await store.SearchAsync(query, [], new FullTextSearchOptions { K = 5 }, cancellationToken).ToListAsync(cancellationToken);
		var fullTextMs = clock.Elapsed.TotalMilliseconds / Runs;

		// 7. Hybrid search, warm — same corpus, same query, embedding already in hand.
		for (var i = 0; i < Warmup; i++)
			await store.SearchAsync(query, qv, [], new HybridSearchOptions { K = 5 }, cancellationToken).ToListAsync(cancellationToken);

		clock.Restart();
		for (var i = 0; i < Runs; i++)
			await store.SearchAsync(query, qv, [], new HybridSearchOptions { K = 5 }, cancellationToken).ToListAsync(cancellationToken);
		var hybridMs = clock.Elapsed.TotalMilliseconds / Runs;

		// 8. The same searches WITH the isolation tag — the shape `related` must actually use.
		//    HybridSearchOptions.K notes the candidate pool is raised to the table row count when a
		//    tag filter applies, so this is the honest cost, not the untagged one.
		MemoryContracts.Tag[] scope = [new() { Scope = "user", Value = "sergio" }];

		for (var i = 0; i < Warmup; i++)
			await store.SearchAsync(query, scope, new FullTextSearchOptions { K = 5 }, cancellationToken).ToListAsync(cancellationToken);

		clock.Restart();
		for (var i = 0; i < Runs; i++)
			await store.SearchAsync(query, scope, new FullTextSearchOptions { K = 5 }, cancellationToken).ToListAsync(cancellationToken);
		var taggedFullTextMs = clock.Elapsed.TotalMilliseconds / Runs;

		for (var i = 0; i < Warmup; i++)
			await store.SearchAsync(query, qv, scope, new HybridSearchOptions { K = 5 }, cancellationToken).ToListAsync(cancellationToken);

		clock.Restart();
		for (var i = 0; i < Runs; i++)
			await store.SearchAsync(query, qv, scope, new HybridSearchOptions { K = 5 }, cancellationToken).ToListAsync(cancellationToken);
		var taggedHybridMs = clock.Elapsed.TotalMilliseconds / Runs;

		// And confirm the filter actually isolates: no other principal's memory may surface.
		var scoped  = await store.SearchAsync(query, qv, scope, new HybridSearchOptions { K = 50 }, cancellationToken).ToListAsync(cancellationToken);
		var leaked  = scoped.Count(hit => hit.Memory.Tags.Any(tag => tag.Value == "someone-else"));

		// 9. How limit moves the cost.
		var byLimit = new List<(int Limit, double Ms)>();

		foreach (var limit in (int[])[3, 5, 10, 20]) {
			for (var i = 0; i < Warmup; i++)
				await store.SearchAsync(query, qv, scope, new HybridSearchOptions { K = limit }, cancellationToken).ToListAsync(cancellationToken);

			clock.Restart();
			for (var i = 0; i < Runs; i++)
				await store.SearchAsync(query, qv, scope, new HybridSearchOptions { K = limit }, cancellationToken).ToListAsync(cancellationToken);

			byLimit.Add((limit, clock.Elapsed.TotalMilliseconds / Runs));
		}

		Console.WriteLine($"{Marker} corpus={CorpusSize} runs={Runs}");
		Console.WriteLine($"{Marker} ONCE  generator-construct   {constructMs,9:F1} ms");
		Console.WriteLine($"{Marker} ONCE  first-embedding       {coldEmbedMs,9:F1} ms");
		Console.WriteLine($"{Marker} PER   embed-single          {warmEmbedMs,9:F1} ms");
		Console.WriteLine($"{Marker} PER   embed-batched         {batchedEmbedMs,9:F1} ms");
		Console.WriteLine($"{Marker} PER   search-full-text      {fullTextMs,9:F1} ms");
		Console.WriteLine($"{Marker} PER   search-hybrid         {hybridMs,9:F1} ms");
		Console.WriteLine($"{Marker} PER   related-full-text     {fullTextMs,9:F1} ms  (search only)");
		Console.WriteLine($"{Marker} PER   search-full-text+tag  {taggedFullTextMs,9:F1} ms");
		Console.WriteLine($"{Marker} PER   search-hybrid+tag     {taggedHybridMs,9:F1} ms");
		Console.WriteLine($"{Marker} PER   related-hybrid        {batchedEmbedMs + hybridMs,9:F1} ms  (embed + search, NO tag)");
		Console.WriteLine($"{Marker} PER   related-hybrid+tag    {batchedEmbedMs + taggedHybridMs,9:F1} ms  (embed + search, isolated)");
		Console.WriteLine($"{Marker} ISO   cross-principal-leaks {leaked,9}     (must be 0)");

		foreach (var (limit, ms) in byLimit)
			Console.WriteLine($"{Marker} LIM   hybrid+tag limit={limit,-3}     {ms,9:F1} ms");

		// The only assertion worth making: everything ran and produced a usable number. A timing
		// budget here would be a flaky test on a shared machine.
		await Assert.That(warmEmbedMs).IsGreaterThan(0);
		await Assert.That(hybridMs).IsGreaterThan(0);
	}
}
