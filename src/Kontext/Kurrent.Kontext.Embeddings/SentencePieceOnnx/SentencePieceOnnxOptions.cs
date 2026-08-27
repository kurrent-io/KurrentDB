// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings.SentencePieceOnnx;

/// <summary>
/// Options for the SentencePiece / XLM-R ONNX embedding generator (implementation C). It runs any model that
/// uses the XLM-RoBERTa SentencePiece tokenizer + fairseq id layout — e.g. multilingual-e5,
/// paraphrase-multilingual, bge-m3. The conventions that vary across that family (input prefix, pooling) are
/// exposed as options. Settable to support the <c>Action&lt;SentencePieceOnnxOptions&gt;</c> pattern.
/// </summary>
public sealed record SentencePieceOnnxOptions {
	/// <summary>How per-token outputs are collapsed. e5 / paraphrase-multilingual use Mean; bge-m3 uses Cls.</summary>
	public EmbeddingPoolingMode PoolingMode { get; set; } = EmbeddingPoolingMode.Mean;

	/// <summary>L2-normalize the pooled vector so cosine similarity reduces to a dot product.</summary>
	public bool NormalizeEmbeddings { get; set; } = true;

	/// <summary>Maximum tokens per input; longer inputs are truncated (XLM-R models cap at 512).</summary>
	public int MaxTokens { get; set; } = 512;

	/// <summary>
	/// Optional text prepended to every input before tokenization, whatever it is being embedded for.
	/// paraphrase-multilingual and bge-m3 use none — leave <see langword="null"/>. Asymmetric models
	/// set <see cref="QueryPrefix"/> and <see cref="DocumentPrefix"/> instead.
	/// </summary>
	public string? InputPrefix { get; set; }

	/// <summary>
	/// Prefix for text being embedded as a search query, overriding <see cref="InputPrefix"/>. The
	/// caller signals this through <see cref="EmbeddingPurpose"/>; the retrieval side already does.
	/// multilingual-e5 wants <c>"query: "</c> here.
	/// </summary>
	public string? QueryPrefix { get; set; }

	/// <summary>
	/// Prefix for text being embedded for storage, overriding <see cref="InputPrefix"/>. This is the
	/// default purpose, so it applies unless a caller says otherwise. multilingual-e5 wants
	/// <c>"passage: "</c> here — embedding a stored memory as a query costs recall.
	/// </summary>
	public string? DocumentPrefix { get; set; }

	/// <summary>
	/// The <see cref="OnnxModel"/> asset name for the XLM-R <c>sentencepiece.bpe.model</c> — read via
	/// <see cref="OnnxModel.ReadAsset"/>. Defaults to <c>sentencepiece.bpe.model</c>.
	/// </summary>
	public string TokenizerAsset { get; set; } = "sentencepiece.bpe.model";

	/// <summary>
	/// Which model to resolve from the <see cref="OnnxModelRegistry"/> (the registry constructor). Required on
	/// that path — there is no default, because each model in this family needs its own pooling mode, window
	/// and prefix. Ignored when an <see cref="OnnxModel"/> is supplied directly — that model's
	/// <see cref="OnnxModel.Name"/> is used as the reported model id.
	/// </summary>
	public string? ModelId { get; set; }
}
