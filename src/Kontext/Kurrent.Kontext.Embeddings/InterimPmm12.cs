// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Models;

namespace Kurrent.Kontext.Embeddings;

/// <summary>
/// The interim production model — paraphrase-multilingual-MiniLM-L12-v2 (pMM12) resolved from the ONNX
/// embedded in the <c>KurrentDB.Kontext.Models</c> assembly. The one definition of which resources and
/// options that model uses: <see cref="InterimEmbeddingsServiceCollectionExtensions"/> registers it, and
/// the suites and benchmarks that must rank on the shipped model create it directly.
/// <para>
/// Goes when the downloader lands, alongside the registration helper and the build-time embed target.
/// </para>
/// </summary>
public static class InterimPmm12 {
	// Stable logical-names of the pMM12 resources embedded by the KurrentDB.Kontext.Models build target.
	const string Name              = "paraphrase-multilingual-MiniLM-L12-v2";
	const string ModelResource     = "KurrentDB.Kontext.Models.pmm12.model.onnx";
	const string TokenizerResource = "KurrentDB.Kontext.Models.pmm12.sentencepiece.bpe.model";

	/// <summary>pMM12 through the SentencePiece / XLM-R generator. pMM12 uses no input prefix.</summary>
	public static SentencePieceOnnxEmbeddingGenerator CreateEmbeddingGenerator(Action<SentencePieceOnnxOptions>? configure = null) {
		// pMM12 was trained at max_seq_length 128 — beyond that, tokens ride position embeddings
		// the model never saw. XLM-R positions physically extend to 512, so the generator's
		// family default runs, but this model's correct window is 128.
		var options = new SentencePieceOnnxOptions { InputPrefix = null, MaxTokens = 128 };
		configure?.Invoke(options);
		return new(Model(), options);
	}

	/// <summary>FromEmbeddedResources is lazy — no bytes move here; the generator opens them when it is built.</summary>
	static OnnxModel Model() =>
		OnnxModel.FromEmbeddedResources(
			Name,
			typeof(KontextModelsAssembly).Assembly,
			ModelResource,
			new Dictionary<string, string> { ["sentencepiece.bpe.model"] = TokenizerResource });
}
