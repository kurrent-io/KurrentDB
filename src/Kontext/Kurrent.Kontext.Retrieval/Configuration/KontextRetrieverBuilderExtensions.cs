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
            ArgumentNullException.ThrowIfNull(index);
            ArgumentNullException.ThrowIfNull(embeddingGenerator);

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
            ArgumentNullException.ThrowIfNull(index);
            ArgumentNullException.ThrowIfNull(embeddingGenerator);

            const int retrievalCandidates = 30;

            return builder
                .Planner(new OverfetchOptions { Factor = 0, Floor = retrievalCandidates }, options.Time)
                .AddSearch(new VectorSearch(index, embeddingGenerator))
                .AddSearch(new KeywordSearch(index))
                .Fuser(ReciprocalRankFuser.Create(static fusion => fusion.Normalize = true))
                .AddStage(MmrReorderer.Create());
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
