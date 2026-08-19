// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Models;

namespace Kurrent.Kontext.Embeddings.SentencePieceOnnx;

/// <summary>
/// paraphrase-multilingual-MiniLM-L12-v2 (pMM12) — the shipped model, read from the ONNX embedded in
/// the <c>KurrentDB.Kontext.Models</c> assembly. The one definition of which resources and options
/// that model uses: <see cref="InterimEmbeddingsServiceCollectionExtensions"/> registers it, and the
/// suites and benchmarks that must rank on the shipped model construct it directly.
/// <para>
/// The embedded resources go when the downloader lands; the model's conventions below do not.
/// </para>
/// </summary>
public sealed class Pmm12EmbeddingGenerator : SentencePieceOnnxEmbeddingGenerator {
	// Stable logical-names of the pMM12 resources embedded by the KurrentDB.Kontext.Models build target.
	const string Name              = "paraphrase-multilingual-MiniLM-L12-v2";
	const string ModelResource     = "KurrentDB.Kontext.Models.pmm12.model.onnx";
	const string TokenizerResource = "KurrentDB.Kontext.Models.pmm12.sentencepiece.bpe.model";

	public Pmm12EmbeddingGenerator(Action<SentencePieceOnnxOptions>? configure = null)
		: base(Model(), Options(configure)) { }

	static SentencePieceOnnxOptions Options(Action<SentencePieceOnnxOptions>? configure) {
		// pMM12 was trained at max_seq_length 128 — beyond that, tokens ride position embeddings
		// the model never saw. XLM-R positions physically extend to 512, so the family default
		// would run, but this model's correct window is 128. pMM12 uses no input prefix.
		var options = new SentencePieceOnnxOptions {
			PoolingMode = EmbeddingPoolingMode.Mean,
			MaxTokens   = 128,
			InputPrefix = null,
		};

		configure?.Invoke(options);

		return options;
	}

	/// <summary>FromEmbeddedResources is lazy — no bytes move here; the base opens them when it is built.</summary>
	static OnnxModel Model() =>
		OnnxModel.FromEmbeddedResources(
			Name,
			typeof(KontextModelsAssembly).Assembly,
			ModelResource,
			new Dictionary<string, string> { ["sentencepiece.bpe.model"] = TokenizerResource });
}
