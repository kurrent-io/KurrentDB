// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// The shipped retrieval chains, beside the parts they compose. These extend
/// <see cref="KontextRetrieverBuilder"/> rather than returning a finished retriever, so a caller
/// can append a stage to a shipped chain without forking it.
/// <para>The host's <c>AddKontextRetrieval</c> resolves services and delegates here, and the
/// suites and benchmarks that measure the product call the same methods. A chain defined in two
/// places drifts; this is the one definition.</para>
/// </summary>
[PublicAPI]
public static class KontextRetrieverBuilderExtensions {
    /// <param name="builder">The builder the chain is applied to.</param>
    extension(KontextRetrieverBuilder builder) {
        /// <summary>The default chain: both legs, rank fusion, BM25 reread, modulation, then MMR.</summary>
        /// <param name="index">The memories read model both search legs query.</param>
        /// <param name="embeddingGenerator">The generator the vector leg embeds the query with — the same model that embedded the stored memories.</param>
        /// <param name="options">The chain's knobs.</param>
        public KontextRetrieverBuilder Default(
            IMemoryIndex index,
            EmbeddingGenerator embeddingGenerator,
            KontextRetrievalOptions options
        ) {
            return builder
                .Planner(options.Overfetch, options.Time)
                .AddSearch(new VectorSearch(index, embeddingGenerator))
                .AddSearch(new KeywordSearch(index))
                .Fuser(ReciprocalRankFuser.Create())
                .AddStage(Bm25Reranker.Create(options.Reranking))
                .AddStage(CognitiveModulator.Create(options.Modulation))
                .AddStage(MmrReorderer.Create(options.Reordering));
        }

        /// <inheritdoc cref="Default(IMemoryIndex,EmbeddingGenerator,KontextRetrievalOptions)"/>
        public KontextRetrieverBuilder Default(
            IMemoryIndex index,
            EmbeddingGenerator embeddingGenerator,
            Action<KontextRetrievalOptions>? configure = null
        ) {
            var options = new KontextRetrievalOptions();
            configure?.Invoke(options);

            return builder.Default(index, embeddingGenerator, options);
        }

        /// <summary>
        /// The hybrid-comparison chain: Lance's in-engine alpha blend as the single search leg,
        /// then the same reread, modulation, and MMR stages as the default chain — so a benchmark
        /// isolates the fusion step: engine alpha blend versus Kontext rank fusion.
        /// </summary>
        /// <param name="index">The memories read model the hybrid leg queries.</param>
        /// <param name="embeddingGenerator">The generator the vector half embeds the query with — the same model that embedded the stored memories.</param>
        /// <param name="options">The chain's knobs; <see cref="KontextRetrievalOptions.Alpha"/> sets the blend.</param>
        public KontextRetrieverBuilder Hybrid(
            IMemoryIndex index,
            EmbeddingGenerator embeddingGenerator,
            KontextRetrievalOptions options
        ) {
            return builder
                .Planner(options.Overfetch, options.Time)
                .AddSearch(new HybridSearch(index, embeddingGenerator, options.Alpha))
                .AddStage(Bm25Reranker.Create(options.Reranking))
                .AddStage(CognitiveModulator.Create(options.Modulation))
                .AddStage(MmrReorderer.Create(options.Reordering));
        }

        /// <inheritdoc cref="Hybrid(IMemoryIndex,EmbeddingGenerator,KontextRetrievalOptions)"/>
        public KontextRetrieverBuilder Hybrid(
            IMemoryIndex index,
            EmbeddingGenerator embeddingGenerator,
            Action<KontextRetrievalOptions>? configure = null
        ) {
            var options = new KontextRetrievalOptions();
            configure?.Invoke(options);

            return builder.Hybrid(index, embeddingGenerator, options);
        }

        /// <summary>
        /// The shipped chain: the engine's alpha blend, BM25 reread, and modulation — no MMR.
        /// Deliberately not configurable: the knobs are the 2026-08-15 LoCoMo hill-climb optimum
        /// (recall@5 0.4889 vs 0.4622 for hybrid α 0.5; the diversity reorder was costing recall),
        /// and a tuned variant is a different chain — compose it with <see cref="Hybrid(IMemoryIndex,EmbeddingGenerator,KontextRetrievalOptions)"/>
        /// or the builder directly.
        /// </summary>
        /// <param name="index">The memories read model the hybrid leg queries.</param>
        /// <param name="embeddingGenerator">The generator the vector half embeds the query with — the same model that embedded the stored memories.</param>
        /// <param name="time">The clock the planner ages candidates against; null uses the system clock.</param>
        public KontextRetrieverBuilder Focused(
            IMemoryIndex index,
            EmbeddingGenerator embeddingGenerator,
            TimeProvider? time = null
        ) {
            const double measuredAlpha = 0.45;

            return builder
                .Planner(new OverfetchOptions(), time)
                .AddSearch(new HybridSearch(index, embeddingGenerator, measuredAlpha))
                .AddStage(Bm25Reranker.Create())
                .AddStage(CognitiveModulator.Create());
        }

        /// <summary>
        /// The legacy chain, kept as the baseline the default is measured against: a fixed
        /// candidate floor, normalized fusion, and no BM25 reread or modulation.
        /// </summary>
        /// <param name="index">The memories read model both search legs query.</param>
        /// <param name="embeddingGenerator">The generator the vector leg embeds the query with — the same model that embedded the stored memories.</param>
        /// <param name="options">The chain's knobs; only <see cref="KontextRetrievalOptions.Time"/> is read.</param>
        public KontextRetrieverBuilder Legacy(
            IMemoryIndex index,
            EmbeddingGenerator embeddingGenerator,
            KontextRetrievalOptions options
        ) {
            const int retrievalCandidates = 30;

            return builder
                .Planner(new OverfetchOptions { Factor = 0, Floor = retrievalCandidates }, options.Time)
                .AddSearch(new VectorSearch(index, embeddingGenerator))
                .AddSearch(new KeywordSearch(index))
                .Fuser(ReciprocalRankFuser.Create(static fusion => fusion.Normalize = true))
                .AddStage(MmrReorderer.Create());
        }

        /// <summary>
        /// The entity-aware chain: <see cref="Focused"/>'s alpha blend plus the entity leg —
        /// memories mentioning the entities the query names — rank-fused, then the same reread
        /// and modulation. Unmeasured against <see cref="Focused"/> on LoCoMo yet: it exists to
        /// put resolved entities into recall, benchmark it before calling it optimal.
        /// </summary>
        /// <param name="index">The memories read model the hybrid leg queries.</param>
        /// <param name="entities">The entity read model the entity leg queries.</param>
        /// <param name="embeddingGenerator">The generator the vector half embeds the query with — the same model that embedded the stored memories.</param>
        /// <param name="time">The clock the planner ages candidates against; null uses the system clock.</param>
        public KontextRetrieverBuilder Connected(
            IMemoryIndex index,
            IEntityIndex entities,
            EmbeddingGenerator embeddingGenerator,
            TimeProvider? time = null
        ) {
            const double measuredAlpha = 0.45;

            return builder
                .Planner(new OverfetchOptions(), time)
                .AddSearch(new HybridSearch(index, embeddingGenerator, measuredAlpha))
                .AddSearch(new EntitySearch(entities))
                .Fuser(ReciprocalRankFuser.Create())
                .AddStage(Bm25Reranker.Create())
                .AddStage(CognitiveModulator.Create());
        }

        /// <inheritdoc cref="Legacy(IMemoryIndex,EmbeddingGenerator,KontextRetrievalOptions)"/>
        public KontextRetrieverBuilder Legacy(
            IMemoryIndex index,
            EmbeddingGenerator embeddingGenerator,
            Action<KontextRetrievalOptions>? configure = null
        ) {
            var options = new KontextRetrievalOptions();
            configure?.Invoke(options);

            return builder.Legacy(index, embeddingGenerator, options);
        }
    }
}
