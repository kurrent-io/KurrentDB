// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Retrieval;
using Serilog;

namespace Kurrent.Kontext.Tests;

/// <summary>
/// Measures the hybrid-comparison chain (Lance's in-engine alpha blend as the single leg) against
/// the default chain (two legs fused by reciprocal rank in Kontext) on the same corpus and ground
/// truth. A measurement harness: the logged table is the deliverable; the assertions only pin
/// that every composition answered every question.
/// </summary>
[Category("Benchmark")]
[Timeout(300_000)]
public class HybridRankingTests {
    [ClassDataSource<KontextCorpus>(Shared = SharedType.PerTestSession)]
    public required KontextCorpus Corpus { get; init; }

    [Test]
    public async ValueTask hybrid_chain_measures_against_the_default_chain(CancellationToken ct) {
        // Arrange
        (string Label, IKontextRetriever Retriever)[] compositions = [
            ("default rrf", KontextRetriever.New().Default(Corpus.Store, Corpus.EmbeddingGenerator).Build()),
            .. from alpha in (double[]) [0.3, 0.4, 0.5]
               select ($"hybrid a={alpha:F1}",
                       KontextRetriever.New().Hybrid(Corpus.Store, Corpus.EmbeddingGenerator, options => options.Alpha = alpha).Build()),
        ];

        var expectedOutcomes = Corpus.Questions.Count;

        // Act + Assert
        foreach (var (label, retriever) in compositions) {
            var outcomes = await Corpus.Evaluate(retriever, ct: ct);

            Log.Information("{Composition}: recall@5 {Recall:F4}, mrr {Mrr:F4}, ndcg@10 {Ndcg:F4}",
                label,
                RankingMetrics.RecallAt(outcomes, 5),
                RankingMetrics.Mrr(outcomes),
                RankingMetrics.NdcgAt(outcomes, 10));

            await Assert.That(outcomes.Count).IsEqualTo(expectedOutcomes);
        }
    }
}
