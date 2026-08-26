// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Records.Data;
using Kurrent.Kontext.Testing;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// PROBE: does an outer WHERE on `stream` / `schema_name` reach INSIDE lance_hybrid_search, or is it
/// applied above the engine's k ranked rows?
///
/// It matters because it decides what the search contract can promise. A post-filter means a scoped
/// search has to oversample and still comes back short; a true prefilter means scoping is free — and,
/// per the memories probe, cheaper than not scoping.
///
/// lance_hybrid_search takes no `filter :=` argument (unlike lance_vector_search and lance_fts), so the
/// only route is DuckDB filter pushdown. The extension opts into it
/// (`~/dev/contrib/lance-duckdb/src/lance_search.cpp:1649` — filter_pushdown, filter_prune,
/// pushdown_complex_filter), which says the machinery exists, not that these two predicates use it.
///
/// The discriminator, same shape as TagPrefilterPushdownProbeTests: bury a SMALL minority under a
/// large, strictly better-matching majority, then ask for a scoped page.
///   - true prefilter => a FULL page of the minority, because ranking never saw the majority
///   - post-filter    => zero, because the top-k is all majority and gets filtered away
/// </summary>
[Category("Integration")]
[Timeout(300_000)]
public class RecordsFilterPushdownProbeTests {
    const int CorpusSize = 300;
    const int MineCount  = 10;
    const int Page       = 5;

    const string Marker = "RECORDS-PUSHDOWN";

    const string MineStream     = "order-mine";
    const string MineSchemaName = "OrderCancelled";

    [Test]
    public async ValueTask stream_and_schema_name_filters_reach_inside_the_engine(CancellationToken cancellationToken) {
        // Arrange
        using var dir         = new TempDir();
        using var dataSources = MemorySeeding.NewDataSources(dir.Path);
        using var embeddings  = new Pmm12EmbeddingGenerator();

        var options = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };
        var query   = "the payment for the order failed because the card expired";

        // The majority restates the query almost verbatim, so unfiltered ranking is entirely theirs.
        // Mine share exactly ONE query term — enough for BM25 to score them above zero, far too little
        // to enter a top-k computed before filtering.
        var texts = Enumerable.Range(0, CorpusSize)
            .Select(i => i < MineCount
                ? $"{{\"note\":\"order {i} concerning marsupial habitats\"}}"
                : $"{{\"note\":\"the payment for the order failed because the card expired, variant {i}\"}}")
            .ToArray();

        var vectors = await embeddings.GenerateAsync(texts, options, cancellationToken);

        var rows = texts
            .Select((content, i) => new RecordRow(
                LogPosition: i,
                RecordId: Guid.CreateVersion7(),
                Stream: i < MineCount ? MineStream : $"order-{i}",
                Category: "order",
                SchemaName: i < MineCount ? MineSchemaName : "OrderPlaced",
                Content: content) {
                Embedding = vectors[i].Vector.ToArray(),
            })
            .ToArray();

        var store = await RecordsSeeding.Seed(dataSources, rows);
        var qv    = (await embeddings.GenerateAsync([query], options, cancellationToken))[0].Vector.ToArray();

        // Act
        var unfiltered = await store
            .SearchAsync(new HybridOptions { Query = query, QueryEmbedding = qv, K = Page }, cancellationToken)
            .ToListAsync(cancellationToken);

        var byStream = await store
            .SearchAsync(new HybridOptions { Query = query, QueryEmbedding = qv, K = Page, Stream = MineStream }, cancellationToken)
            .ToListAsync(cancellationToken);

        var bySchemaName = await store
            .SearchAsync(new HybridOptions { Query = query, QueryEmbedding = qv, K = Page, SchemaName = MineSchemaName }, cancellationToken)
            .ToListAsync(cancellationToken);

        Console.WriteLine($"{Marker} corpus={CorpusSize} mine={MineCount} page={Page}");
        Console.WriteLine($"{Marker} unfiltered-hits        {unfiltered.Count}");
        Console.WriteLine($"{Marker} unfiltered-any-mine    {unfiltered.Count(h => h.Record.Stream == MineStream)}");
        Console.WriteLine($"{Marker} stream-scoped-hits     {byStream.Count}");
        Console.WriteLine($"{Marker} schema-scoped-hits     {bySchemaName.Count}");

        // Assert — the majority owns the unfiltered ranking, so a full scoped page is only possible if
        // the predicate ran BEFORE ranking.
        await Assert.That(unfiltered.Count).IsEqualTo(Page);
        await Assert.That(unfiltered.Any(h => h.Record.Stream == MineStream)).IsFalse();

        await Assert.That(byStream.Count).IsEqualTo(Page);
        await Assert.That(byStream.All(h => h.Record.Stream == MineStream)).IsTrue();

        await Assert.That(bySchemaName.Count).IsEqualTo(Page);
        await Assert.That(bySchemaName.All(h => h.Record.SchemaName == MineSchemaName)).IsTrue();
    }

    /// <summary>
    /// The properties JSON survives the round trip and reaches the contract as a map. It is returned,
    /// never searched — this pins that it is not silently dropped on the way out.
    /// </summary>
    [Test]
    public async ValueTask properties_come_back_on_a_hit(CancellationToken cancellationToken) {
        // Arrange
        using var dir         = new TempDir();
        using var dataSources = MemorySeeding.NewDataSources(dir.Path);
        using var embeddings  = new Pmm12EmbeddingGenerator();

        var options = new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension };
        var content = "{\"note\":\"the order was cancelled\"}";
        var vector  = (await embeddings.GenerateAsync([content], options, cancellationToken))[0].Vector.ToArray();

        var store = await RecordsSeeding.Seed(dataSources, new RecordRow(
            LogPosition: 1,
            RecordId: Guid.CreateVersion7(),
            Stream: "order-1",
            Category: "order",
            SchemaName: MineSchemaName,
            Content: content) {
            Embedding  = vector,
            Properties = """{"$correlationId":"abc","tenant":"acme"}""",
        });

        // Act
        var hits = await store
            .SearchAsync(new HybridOptions { Query = "the order was cancelled", QueryEmbedding = vector, K = 5 }, cancellationToken)
            .ToListAsync(cancellationToken);

        // Assert
        await Assert.That(hits.Count).IsEqualTo(1);
        await Assert.That(hits[0].Record.Properties).IsEqualTo("""{"$correlationId":"abc","tenant":"acme"}""");
    }
}
