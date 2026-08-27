// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Testing;

namespace Benchmarks;

/// <summary>
/// paraphrase-multilingual-mpnet-base-v2 — pMM12's larger sibling: same family, same training recipe,
/// same XLM-R tokenizer, 768 dimensions instead of 384. Fetched on first run rather than at build
/// time, because nothing in Kontext runs on it.
/// </summary>
/// <remarks>
/// Both repos publish the SentencePiece model beside the ONNX, so unlike pMM12 no leg here has to
/// borrow its tokenizer from another model.
/// </remarks>
public sealed class PmpnetEmbeddingGenerator : SentencePieceOnnxEmbeddingGenerator {
	const string TokenizerFile = "sentencepiece.bpe.model";
	const string Xenova        = "https://huggingface.co/Xenova/paraphrase-multilingual-mpnet-base-v2/resolve/main";
	const string Official      = "https://huggingface.co/sentence-transformers/paraphrase-multilingual-mpnet-base-v2/resolve/main";

	public PmpnetEmbeddingGenerator(Action<SentencePieceOnnxOptions>? configure = null)
		: this(OnnxExport.Int8Partial, configure) { }

	public PmpnetEmbeddingGenerator(OnnxExport export, Action<SentencePieceOnnxOptions>? configure = null)
		: base(Model(export), Options(configure)) { }

	static SentencePieceOnnxOptions Options(Action<SentencePieceOnnxOptions>? configure) {
		// Straight off the model card: max_seq_length 128, mean pooling, no input prefix — the same
		// settings pMM12 uses, which is what leaves the backbone as the only variable.
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
			OnnxExport.Int8Full    => (Official, "model_qint8_arm64.onnx"),
			_ => throw new ArgumentOutOfRangeException(nameof(export), export, "Unknown pMPNet export."),
		};

		var key       = $"pmpnet-{export}".ToLowerInvariant();
		var directory = ModelCache.Ensure(BenchmarkModels.Directory(key), [
			($"{repo}/onnx/{file}", file),
			($"{repo}/{TokenizerFile}", TokenizerFile),
		]);

		return OnnxModel.FromFiles(
			key,
			Path.Combine(directory, file),
			new Dictionary<string, string> { [TokenizerFile] = Path.Combine(directory, TokenizerFile) });
	}
}
