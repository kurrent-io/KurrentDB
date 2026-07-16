// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings.PrototypeV2;

/// <summary>
/// Options for the default local embedding generator (implementation A) — the hand-rolled WordPiece +
/// all-MiniLM-L6-v2 path that ships as the default and stays byte-faithful to vectors already stored in
/// existing indexes. There is deliberately no case/normalization knob: the tokenizer's behaviour is fixed
/// so its output can never drift from what is already indexed. Properties are settable to support the
/// <c>Action&lt;PrototypeV2Options&gt;</c> configuration pattern.
/// </summary>
public sealed record PrototypeV2Options {
	/// <summary>How per-token outputs are collapsed into one vector. The shipped default is mean pooling.</summary>
	public EmbeddingPoolingMode PoolingMode { get; set; } = EmbeddingPoolingMode.Mean;

	/// <summary>L2-normalize the pooled vector, as the shipped default does.</summary>
	public bool NormalizeEmbeddings { get; set; } = true;

	/// <summary>Maximum tokens per input; longer inputs are truncated (all-MiniLM's limit is 512).</summary>
	public int MaxTokens { get; set; } = 512;

	/// <summary>Model id reported via <c>EmbeddingGeneratorMetadata</c>; defaults to <c>all-MiniLM-L6-v2</c> when null.</summary>
	public string? ModelId { get; set; }
}
