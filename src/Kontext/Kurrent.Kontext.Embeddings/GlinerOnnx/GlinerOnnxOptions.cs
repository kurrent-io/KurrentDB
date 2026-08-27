// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Embeddings.GlinerOnnx;

/// <summary>
/// Options for the GLiNER ONNX entity recognizer. It runs any span-mode GLiNER export on a
/// DeBERTa-v3 backbone (gliner_small/medium/large v2.x); the knobs that vary across that family
/// (token budget, span width, prompt-token ids) are exposed here. Settable to support the
/// <c>Action&lt;GlinerOnnxOptions&gt;</c> pattern and configuration binding.
/// </summary>
public sealed record GlinerOnnxOptions {
	/// <summary>Sigmoid score floor — spans scoring below it never come back.</summary>
	public double Threshold { get; set; } = 0.5;

	/// <summary>Widest span the model scores, in words (GLiNER's <c>max_width</c>).</summary>
	public int MaxWidth { get; set; } = 12;

	/// <summary>
	/// Maximum tokens per input (GLiNER's <c>max_len</c>). The label prompt always rides in full;
	/// trailing words are dropped to fit.
	/// </summary>
	public int MaxTokens { get; set; } = 384;

	/// <summary>
	/// The <see cref="OnnxModel"/> asset name for the backbone's SentencePiece model — read via
	/// <see cref="OnnxModel.ReadAsset"/>. Defaults to <c>spm.model</c>.
	/// </summary>
	public string TokenizerAsset { get; set; } = "spm.model";

	/// <summary>Id of the <c>&lt;&lt;ENT&gt;&gt;</c> prompt token — 128002 across the DeBERTa-v3 GLiNER family.</summary>
	public int EntityTokenId { get; set; } = 128002;

	/// <summary>Id of the <c>&lt;&lt;SEP&gt;&gt;</c> prompt token — 128003 across the DeBERTa-v3 GLiNER family.</summary>
	public int PromptSeparatorTokenId { get; set; } = 128003;

	/// <summary>
	/// Which model to resolve from the <see cref="OnnxModelRegistry"/> (the registry constructor);
	/// defaults to <c>gliner-small-v2.1</c>. Ignored when an <see cref="OnnxModel"/> is supplied
	/// directly.
	/// </summary>
	public string? ModelId { get; set; }
}
