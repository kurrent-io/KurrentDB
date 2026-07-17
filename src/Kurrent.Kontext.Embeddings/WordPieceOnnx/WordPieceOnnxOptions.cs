// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings.WordPieceOnnx;

/// <summary>
/// Options for a BERT/WordPiece ONNX embedding generator (implementation B, e.g. all-MiniLM-L6-v2).
/// The defaults reproduce the sentence-transformers pipeline validated against the hand-rolled path.
/// Properties are settable to support the <c>Action&lt;WordPieceOnnxOptions&gt;</c> configuration pattern.
/// </summary>
public sealed record WordPieceOnnxOptions {
	/// <summary>How per-token outputs are collapsed into one vector. Sentence-transformers uses mean pooling.</summary>
	public EmbeddingPoolingMode PoolingMode { get; set; } = EmbeddingPoolingMode.Mean;

	/// <summary>L2-normalize the pooled vector so cosine similarity reduces to a dot product.</summary>
	public bool NormalizeEmbeddings { get; set; } = true;

	/// <summary>Maximum tokens per input; longer inputs are truncated (all-MiniLM's max_position_embeddings is 512).</summary>
	public int MaxTokens { get; set; } = 512;

	/// <summary>
	/// The <see cref="OnnxModel"/> asset name for the WordPiece vocab file — read via
	/// <see cref="OnnxModel.ReadAsset"/>. Defaults to <c>vocab.txt</c>.
	/// </summary>
	public string VocabAsset { get; set; } = "vocab.txt";

	/// <summary>
	/// Which model to resolve from the <see cref="OnnxModelRegistry"/> (the registry constructor); defaults to
	/// <c>all-MiniLM-L6-v2</c>. Ignored when an <see cref="OnnxModel"/> is supplied directly — that model's
	/// <see cref="OnnxModel.Name"/> is used as the reported model id.
	/// </summary>
	public string? ModelId { get; set; }
}
