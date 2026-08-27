// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings.Chunking;

/// <summary>
/// Splits text into pieces that each fit the embedding model's window.
/// </summary>
/// <remarks>
/// Text already inside the window comes back as a single chunk, so a caller never branches on
/// whether chunking applied — a short memory and a long one take the same path, and the rare case
/// cannot rot.
/// </remarks>
public interface ITextChunker {
    /// <summary>Splits <paramref name="text"/> into one or more chunks. Never returns empty.</summary>
    IReadOnlyList<string> Chunk(string text);
}
