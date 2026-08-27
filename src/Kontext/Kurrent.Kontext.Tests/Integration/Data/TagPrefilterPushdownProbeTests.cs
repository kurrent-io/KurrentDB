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
/// PROBE: is a tag filter a TRUE PREFILTER, or is it applied above a ranked candidate pool?
///
/// The options docs on <c>FullTextSearchOptions.K</c> and <c>HybridSearchOptions.K</c> claim the
/// pool is "raised to the table's row count when tag filters apply (containment is not pushed
/// down)". The store's own SQL says the opposite — "non-empty containment pushes down as a true
/// prefilter". One of them is stale, and the answer decides whether `related` can scope by the
/// isolation tag without scanning the table.
///
/// The discriminator: bury a SMALL tagged minority under a large, strictly better-matching
/// majority, then ask for a tag-scoped page with the DEFAULT K.
///   - true prefilter  => a full page of the minority, because ranking only ever sees them
///   - post-filter     => few or none, because the top-K is all majority and gets filtered away
/// </summary>
[Category("Integration")]
[Timeout(300_000)]
public class TagPrefilterPushdownProbeTests {
	const int CorpusSize = 1000;
	const int MineCount  = 10;
	const int Limit      = 5;
	const int Runs       = 10;

	const string Marker = "TAG-PUSHDOWN";

	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask tag_filter_is_a_true_prefilter_not_a_post_filter(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var embeddings  = new Pmm12EmbeddingGenerator();

		var options = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };
		var query   = "checkpoint format decision for the projector";

		// The majority restates the query almost verbatim, so unfiltered ranking is entirely theirs.
		// Mine share exactly ONE query term — enough for BM25 to score them above zero, far too
		// little to enter a top-K computed before filtering. An earlier draft gave mine no shared
		// term at all, which made the full-text leg return nothing for a reason that had nothing to
		// do with pushdown: BM25 cannot rank rows that match no term, prefilter or not.
		var texts = Enumerable.Range(0, CorpusSize)
			.Select(i => i < MineCount
				? $"checkpoint notes {i} concerning marsupial habitats"
				: $"checkpoint format decision for the projector, variant {i}")
			.ToArray();

		var vectors = await embeddings.GenerateAsync(texts, options, cancellationToken);

		var mine   = KontextMemoryDataStore.EncodeTag(new MemoryContracts.Tag { Scope = "user", Value = "sergio" });
		var theirs = KontextMemoryDataStore.EncodeTag(new MemoryContracts.Tag { Scope = "user", Value = "someone-else" });

		var rows = texts
			.Select((content, i) => new MemoryRow($"m{i}", MemoryContracts.MemoryType.Fact, content, MemoryContracts.MemoryImportance.Normal, Base) {
				Embedding = vectors[i].Vector.ToArray(),
				Tags      = [i < MineCount ? mine : theirs],
			})
			.ToArray();

		var store = await MemorySeeding.Seed(dataSources, rows);
		var qv    = (await embeddings.GenerateAsync([query], options, cancellationToken))[0].Vector.ToArray();

		MemoryContracts.Tag[] scope = [new() { Scope = "user", Value = "sergio" }];

		// Act — DEFAULT K (10). If containment were post-applied, the pool would be all-majority.
		var unfiltered = await store
			.SearchAsync(query, qv, [], new HybridSearchOptions { K = Limit }, cancellationToken)
			.ToListAsync(cancellationToken);

		var scoped = await store
			.SearchAsync(query, qv, scope, new HybridSearchOptions { K = Limit }, cancellationToken)
			.ToListAsync(cancellationToken);

		var scopedFts = await store
			.SearchAsync(query, scope, new FullTextSearchOptions { K = Limit }, cancellationToken)
			.ToListAsync(cancellationToken);

		// Cost at this corpus size, to extend the 200-row picture.
		var clock = Stopwatch.StartNew();
		for (var i = 0; i < Runs; i++)
			await store.SearchAsync(query, qv, [], new HybridSearchOptions { K = Limit }, cancellationToken).ToListAsync(cancellationToken);
		var untaggedMs = clock.Elapsed.TotalMilliseconds / Runs;

		clock.Restart();
		for (var i = 0; i < Runs; i++)
			await store.SearchAsync(query, qv, scope, new HybridSearchOptions { K = Limit }, cancellationToken).ToListAsync(cancellationToken);
		var taggedMs = clock.Elapsed.TotalMilliseconds / Runs;

		Console.WriteLine($"{Marker} corpus={CorpusSize} mine={MineCount} limit={Limit} defaultK=10");
		Console.WriteLine($"{Marker} unfiltered-hits       {unfiltered.Count}");
		Console.WriteLine($"{Marker} unfiltered-any-mine   {unfiltered.Count(h => h.Memory.MemoryId.StartsWith("m") && int.Parse(h.Memory.MemoryId[1..]) < MineCount)}");
		Console.WriteLine($"{Marker} scoped-hybrid-hits    {scoped.Count}");
		Console.WriteLine($"{Marker} scoped-fts-hits       {scopedFts.Count}");
		Console.WriteLine($"{Marker} search-hybrid         {untaggedMs,7:F1} ms  (no tag)");
		Console.WriteLine($"{Marker} search-hybrid+tag     {taggedMs,7:F1} ms  (prefiltered)");

		// Assert — the majority owns the unfiltered ranking, so the scoped page can only be full if
		// the filter ran BEFORE ranking.
		await Assert.That(unfiltered.Count).IsEqualTo(Limit);
		await Assert.That(unfiltered.All(h => int.Parse(h.Memory.MemoryId[1..]) >= MineCount)).IsTrue();

		await Assert.That(scoped.Count).IsEqualTo(Limit);
		await Assert.That(scoped.All(h => int.Parse(h.Memory.MemoryId[1..]) < MineCount)).IsTrue();

		await Assert.That(scopedFts.Count).IsEqualTo(Limit);
		await Assert.That(scopedFts.All(h => int.Parse(h.Memory.MemoryId[1..]) < MineCount)).IsTrue();
	}
}
