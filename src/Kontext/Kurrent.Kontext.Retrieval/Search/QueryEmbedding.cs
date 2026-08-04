// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;
using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;

namespace Kurrent.Kontext.Retrieval;

/// <summary>
/// Embeds a query with the same generator that embedded the stored memories, flagging the text as
/// a search QUERY (not a stored document) for models that encode the two differently — via the
/// same "purpose" key the storage side uses.
/// </summary>
public static class QueryEmbedding {
    static readonly EmbeddingGenerationOptions AsQuery = new() {
        AdditionalProperties = new() { ["purpose"] = "query" },
    };

    public static async ValueTask<float[]> EmbedQueryAsync(
        this EmbeddingGenerator generator, string text, CancellationToken ct = default) {
        var embedding = await generator.GenerateAsync(text, AsQuery, ct).ConfigureAwait(false);
        return embedding.Vector.ToArray();
    }
}
