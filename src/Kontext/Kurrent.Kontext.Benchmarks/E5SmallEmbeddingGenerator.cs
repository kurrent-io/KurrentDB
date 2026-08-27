// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Testing;

namespace Benchmarks;

/// <summary>
/// multilingual-e5-small — 384 dimensions, so it swaps in against the shipped model with no schema
/// change.
/// </summary>
/// <remarks>
/// Worth measuring here despite scoring near-last on the playground's word-pair eval. That metric is
/// (similar − unrelated) in raw cosine, and e5 compresses everything into a narrow band: its
/// UNRELATED pairs sit around 0.83 where pMM12 puts them at 0.12. recall@k reads rank order, not the
/// size of the gap, so it is immune to the compression that eval punishes.
/// </remarks>
public sealed class E5SmallEmbeddingGenerator : SentencePieceOnnxEmbeddingGenerator {
	const string Name          = "multilingual-e5-small";
	const string ModelFile     = "model_quantized.onnx";
	const string TokenizerFile = "sentencepiece.bpe.model";

	public E5SmallEmbeddingGenerator(Action<SentencePieceOnnxOptions>? configure = null)
		: base(Model(), Options(configure)) { }

	static SentencePieceOnnxOptions Options(Action<SentencePieceOnnxOptions>? configure) {
		// e5 is asymmetric: it was trained with "query: " on the search side and "passage: " on the
		// stored side, and using one prefix for both is a measurable recall loss, not a cosmetic
		// detail.
		var options = new SentencePieceOnnxOptions {
			PoolingMode    = EmbeddingPoolingMode.Mean,
			QueryPrefix    = "query: ",
			DocumentPrefix = "passage: ",
		};

		configure?.Invoke(options);

		return options;
	}

	static OnnxModel Model() {
		var directory = ModelCache.Ensure(BenchmarkModels.Directory("e5-small"), [
			($"https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/onnx/{ModelFile}", ModelFile),
			($"https://huggingface.co/intfloat/multilingual-e5-small/resolve/main/{TokenizerFile}", TokenizerFile),
		]);

		return OnnxModel.FromFiles(
			Name,
			Path.Combine(directory, ModelFile),
			new Dictionary<string, string> { [TokenizerFile] = Path.Combine(directory, TokenizerFile) });
	}
}
