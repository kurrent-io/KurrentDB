// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings;

/// <summary>
/// How per-token model outputs are collapsed into a single sentence embedding.
/// </summary>
public enum EmbeddingPoolingMode {
	/// <summary>
	/// Average the per-token vectors over the attention mask (the sentence-transformers default;
	/// used by all-MiniLM and multilingual-e5).
	/// </summary>
	Mean,

	/// <summary>
	/// Take the first token's vector (the <c>[CLS]</c>/<c>&lt;s&gt;</c> representation). Used by models
	/// trained for CLS pooling such as bge-m3.
	/// </summary>
	Cls,
}
