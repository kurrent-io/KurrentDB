// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Microsoft.Extensions.AI;
using Microsoft.ML.Tokenizers;
using SemanticKernelChunker = Microsoft.SemanticKernel.Text.TextChunker;

// TextChunker is [Experimental("SKEXP0050")]. Suppressed deliberately: it is used for its separator
// ladder — newlines, then sentences, then words — which fills 91-94% of the window against 42-57%
// for the Microsoft.Extensions.DataIngestion chunkers on our content. It is NOT trusted to bound the
// window; see Chunk. Microsoft is moving this surface to DataIngestion, so the migration waits until
// the fill gap closes.
#pragma warning disable SKEXP0050

namespace Kurrent.Kontext.Embeddings.Chunking;

/// <summary>
/// Cuts text on the model's own token count, so no chunk is silently truncated at embedding time.
/// </summary>
/// <remarks>
/// The tokenizer comes from the generator rather than being loaded separately. The question a
/// chunker asks — "will this fit?" — is only meaningful about the tokenizer that will actually
/// encode the text, and a second instance is a second answer waiting to disagree.
/// </remarks>
public sealed class TokenTextChunker : ITextChunker {
    readonly Tokenizer _tokenizer;
    readonly int       _maxTokens;
    readonly int       _overlapTokens;
    readonly string?   _chunkHeader;

    public TokenTextChunker(IEmbeddingGenerator generator, TextChunkerOptions options) {
        _tokenizer = generator.GetRequiredService<Tokenizer>();

        _maxTokens = options.MaxTokens > 0
            ? options.MaxTokens
            : generator.GetService<SentencePieceOnnxOptions>()?.MaxTokens
              ?? throw new InvalidOperationException(
                  $"{nameof(TextChunkerOptions)}.{nameof(TextChunkerOptions.MaxTokens)} is unset and the "
                + "generator does not report a window. Set it to the model's trained sequence length.");

        _overlapTokens = options.OverlapTokens;
        _chunkHeader   = options.ChunkHeader;
    }

    public IReadOnlyList<string> Chunk(string text) {
        if (string.IsNullOrWhiteSpace(text))
            return [text];

        // Split on lines first: JsonNormalizer emits one "key: value" per line and prose arrives as
        // sentences, so lines are a real boundary in both shapes. The chunker descends from there on
        // its own when a line does not fit.
        var chunks = SemanticKernelChunker.SplitPlainTextParagraphs(
            text.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            _maxTokens,
            _overlapTokens,
            _chunkHeader,
            chunk => _tokenizer.CountTokens(chunk));

        // SplitPlainTextParagraphs treats the limit as a target, not a bound: it counts while it
        // accumulates lines, then joins them, and the joined text measures larger. A chunk header is
        // added on top of the budget rather than out of it. Both overshoot by ~10% on real content,
        // which the model would then truncate silently — the exact failure chunking exists to remove.
        List<string> bounded = [];
        foreach (var chunk in chunks)
            bounded.AddRange(_tokenizer.CountTokens(chunk) <= _maxTokens ? [chunk] : Enforce(chunk));

        // A single line of pure whitespace can reduce to nothing; the contract says never empty.
        return bounded.Count > 0 ? bounded : [text];
    }

    /// <summary>
    /// Re-splits an oversized chunk with the library's own line splitter, which descends to word
    /// boundaries, then reattaches the header to each piece.
    /// </summary>
    /// <remarks>
    /// Applying the bound AFTER packing rather than before is the whole difference: splitting every
    /// line up front first, as the two-step call does, costs about a third more chunks — and a chunk
    /// is a vector every query compares against.
    /// </remarks>
    IEnumerable<string> Enforce(string chunk) {
        var header = _chunkHeader ?? "";
        var body   = chunk.StartsWith(header, StringComparison.Ordinal) ? chunk[header.Length..] : chunk;

        // The header rides in every piece, so the body has to fit in what is left of the window.
        var budget = _maxTokens - _tokenizer.CountTokens(header);

        return SemanticKernelChunker
            .SplitPlainTextLines(body, budget, text => _tokenizer.CountTokens(text))
            .Select(line => header + line);
    }
}
