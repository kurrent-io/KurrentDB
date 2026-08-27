// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Testing;

namespace Benchmarks;

/// <summary>
/// The weights the node ships, at every export precision they are published at. Paired with
/// <see cref="Pmm12EmbeddingGenerator"/> — which loads the embedded int8 copy the node actually runs
/// — these legs isolate what the export choice costs, with the model held constant.
/// </summary>
/// <remarks>
/// The tokenizer comes from the model's own sentence-transformers repo, because the Xenova mirror
/// publishes only tokenizer.json. Verified byte-identical to the file the node loads
/// (sha256 cfc8146a…), so these legs differ from the shipped one in precision alone.
/// </remarks>
public sealed class Pmm12VariantEmbeddingGenerator : SentencePieceOnnxEmbeddingGenerator {
	const string TokenizerFile = "sentencepiece.bpe.model";
	const string Xenova        = "https://huggingface.co/Xenova/paraphrase-multilingual-MiniLM-L12-v2/resolve/main";
	const string Official      = "https://huggingface.co/sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2/resolve/main";

	public Pmm12VariantEmbeddingGenerator(OnnxExport export, Action<SentencePieceOnnxOptions>? configure = null)
		: base(Model(export), Options(configure)) { }

	static SentencePieceOnnxOptions Options(Action<SentencePieceOnnxOptions>? configure) {
		var options = new SentencePieceOnnxOptions {
			PoolingMode = EmbeddingPoolingMode.Mean,
			MaxTokens   = 128,
			InputPrefix = null,
		};

		configure?.Invoke(options);

		return options;
	}

	static OnnxModel Model(OnnxExport export) {
		var (repo, file) = export switch {
			OnnxExport.Fp32        => (Xenova,   "model.onnx"),
			OnnxExport.Fp16        => (Xenova,   "model_fp16.onnx"),
			OnnxExport.Int8Partial => (Xenova,   "model_quantized.onnx"),
			OnnxExport.Uint8       => (Xenova,   "model_uint8.onnx"),
			OnnxExport.Q4          => (Xenova,   "model_q4.onnx"),
			OnnxExport.Int8Full    => (Official, "model_quint8_avx2.onnx"),
			_ => throw new ArgumentOutOfRangeException(nameof(export), export, "Unknown pMM12 export."),
		};

		var key       = $"pmm12-{export}".ToLowerInvariant();
		var directory = ModelCache.Ensure(BenchmarkModels.Directory(key), [
			($"{repo}/onnx/{file}", file),
			($"{Official}/{TokenizerFile}", TokenizerFile),
		]);

		return OnnxModel.FromFiles(
			key,
			Path.Combine(directory, file),
			new Dictionary<string, string> { [TokenizerFile] = Path.Combine(directory, TokenizerFile) });
	}
}
