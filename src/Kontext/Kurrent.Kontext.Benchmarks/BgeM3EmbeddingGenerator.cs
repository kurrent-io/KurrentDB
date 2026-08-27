// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Testing;

namespace Benchmarks;

/// <summary>
/// The bge-m3 upgrade candidate through the SentencePiece / XLM-R generator. Nothing in Kontext
/// runs on it, so its ~570MB INT8 export is fetched here the first time this benchmark runs rather
/// than on every build.
/// </summary>
public sealed class BgeM3EmbeddingGenerator : SentencePieceOnnxEmbeddingGenerator {
	const string Name          = "bge-m3";
	const string ModelFile     = "BAAI-bge-m3_quantized.onnx";
	const string TokenizerFile = "sentencepiece.bpe.model";

	public BgeM3EmbeddingGenerator(Action<SentencePieceOnnxOptions>? configure = null)
		: base(Model(), Options(configure)) { }

	static SentencePieceOnnxOptions Options(Action<SentencePieceOnnxOptions>? configure) {
		// bge-m3 is trained for CLS pooling — mean pooling reads a representation it never
		// optimized. Its trained window is max_seq_length 8192, inside the 8194 positions the
		// checkpoint physically carries. bge-m3 uses no input prefix.
		var options = new SentencePieceOnnxOptions {
			PoolingMode = EmbeddingPoolingMode.Cls,
			MaxTokens   = 8192,
			InputPrefix = null,
		};

		configure?.Invoke(options);

		return options;
	}

	static OnnxModel Model() {
		var directory = ModelCache.Ensure(BenchmarkModels.Directory("bgem3"), [
			($"https://huggingface.co/hotchpotch/vespa-onnx-BAAI-bge-m3-only-dense/resolve/main/{ModelFile}", ModelFile),
			($"https://huggingface.co/intfloat/multilingual-e5-small/resolve/main/{TokenizerFile}", TokenizerFile),
		]);

		// This export emits last_hidden_state [batch, tokens, 1024] as output 0 — the shape the
		// Cls path pools. Exports that emit a pre-pooled dense_vecs [batch, 1024] instead
		// (e.g. gpahal/bge-m3-onnx-int8) do NOT run through this path.
		return OnnxModel.FromFiles(
			Name,
			Path.Combine(directory, ModelFile),
			new Dictionary<string, string> { [TokenizerFile] = Path.Combine(directory, TokenizerFile) });
	}
}
