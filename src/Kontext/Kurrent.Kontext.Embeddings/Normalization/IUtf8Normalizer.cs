// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings.Normalization;

/// <summary>Normalizes a UTF-8 payload into the string a downstream consumer embeds or indexes.</summary>
public interface IUtf8Normalizer {
    /// <summary>
    /// Normalizes the input. Content the normalizer does not recognize passes through unchanged;
    /// input with nothing to normalize returns <see langword="null"/>.
    /// </summary>
    string? Normalize(ReadOnlySpan<byte> utf8);
}
