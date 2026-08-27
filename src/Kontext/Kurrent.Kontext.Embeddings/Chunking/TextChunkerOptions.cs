// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings.Chunking;

/// <summary>
/// How text is cut for a given model. A plain mutable settings class so it binds from configuration.
/// </summary>
public sealed class TextChunkerOptions {
    /// <summary>
    /// The model's window. Left at 0 the chunker reads it from the generator, which is the only
    /// value that cannot drift: set it by hand and a model swap silently reintroduces truncation.
    /// </summary>
    public int MaxTokens { get; set; }

    /// <summary>
    /// Tokens repeated from the end of the previous chunk. Zero for flattened records, where each
    /// line is a self-contained fact and overlap buys nothing but vectors; a small overlap for prose,
    /// where a split lands mid-thought.
    /// </summary>
    public int OverlapTokens { get; set; }

    /// <summary>
    /// Text prepended to EVERY chunk before embedding — a memory's title, a record's schema name.
    /// It is not a chunk of its own; it rides inside each one so a chunk that reads "…which is why he
    /// stopped" still carries whose claim it is. Costs its own tokens out of every chunk's budget.
    /// </summary>
    public string? ChunkHeader { get; set; }
}
