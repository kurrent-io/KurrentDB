// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Microsoft.Extensions.AI;
using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;

namespace Kurrent.Kontext.Testing;

/// <summary>
/// The one REAL embedding model the suites share. There are no fake generators: a test embeds with
/// the same model the node runs, at the configured
/// <see cref="KontextIndexConstants.VectorsDimension"/>, so vectors are always the right width and
/// a test never asserts against a shape production cannot produce.
/// </summary>
/// <remarks>
/// Shared and never disposed on purpose: loading the ONNX session costs far more than any single
/// test, and the process exit reclaims it. Access is lazy so a suite that never embeds never pays.
/// </remarks>
public static class KontextTestEmbeddings {
    static readonly Lazy<SentencePieceOnnxEmbeddingGenerator> Instance =
        new(() => new Pmm12EmbeddingGenerator(null), LazyThreadSafetyMode.ExecutionAndPublication);

    public static EmbeddingGenerator Model => Instance.Value;

    public static EmbeddingGenerationOptions Options => new() { Dimensions = KontextIndexConstants.VectorsDimension };

    /// <summary>
    /// The vector the model produces for this content — what a test compares the stored embedding
    /// against, computed the same way the writer computed it.
    /// </summary>
    public static async ValueTask<float[]> Embed(string content, CancellationToken ct = default) {
        var generated = await Model.GenerateAsync([content], Options, ct).ConfigureAwait(false);

        return generated[0].Vector.ToArray();
    }
}
