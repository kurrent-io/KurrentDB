// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings;
using Kurrent.Kontext.Embeddings.Aws;
using Kurrent.Kontext.Embeddings.GoogleVertexAI;
using Kurrent.Kontext.Embeddings.Ollama;
using Kurrent.Kontext.Embeddings.OpenAI;

namespace Kurrent.Kontext.Configuration;

/// <summary>
/// The <c>Embeddings</c> section of <see cref="KontextOptions"/> — Kontext config that selects
/// among the embeddings library's providers. One discriminator, one typed block per provider;
/// a config file carries only the active provider's block.
/// </summary>
public sealed class KontextEmbeddingsOptions {
    public EmbeddingsProvider Provider { get; set; } = EmbeddingsProvider.Local;

    /// <summary>
    /// The vector dimension — the N in the schema's FLOAT[N] column. Must match what the selected
    /// provider's model produces; the bootstrap verifies it with a probe embedding and fails fast
    /// on mismatch, because a wrong dimension poisons every stored vector.

    public LocalEmbeddingsOptions          Local          { get; set; } = new();
    public OpenAIEmbeddingsOptions         OpenAI         { get; set; } = new();
    public OllamaEmbeddingsOptions         Ollama         { get; set; } = new();
    public GoogleVertexAIEmbeddingsOptions GoogleVertexAI { get; set; } = new();
    public AmazonBedrockEmbeddingsOptions  AmazonBedrock  { get; set; } = new();

    /// <summary>The active provider's batch size — encodes each backend's real API limits.</summary>
    public int BatchSize => Provider switch {
        EmbeddingsProvider.Local          => Local.BatchSize,
        EmbeddingsProvider.OpenAI         => OpenAI.BatchSize,
        EmbeddingsProvider.Ollama         => Ollama.BatchSize,
        EmbeddingsProvider.GoogleVertexAI => GoogleVertexAI.BatchSize,
        EmbeddingsProvider.AmazonBedrock  => AmazonBedrock.BatchSize,
        _                                 => 100,
    };
}
