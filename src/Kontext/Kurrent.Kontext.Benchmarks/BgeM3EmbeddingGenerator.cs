// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;

namespace Benchmarks;

/// <summary>
/// The bge-m3 upgrade candidate through the SentencePiece / XLM-R generator. The INT8 export is
/// ~570MB, so <c>KurrentDB.Kontext.Models</c> downloads it under <c>-p:KontextIncludeBgeM3=true</c>
/// rather than embedding it, and this project copies it to the build output.
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
		var directory = Path.Combine(AppContext.BaseDirectory, "Models", "bgem3");

		if (!File.Exists(Path.Combine(directory, ModelFile)))
			throw new InvalidOperationException(
				$"{ModelFile} is not in the build output. Build with -p:KontextIncludeBgeM3=true to download it.");

		// This export emits last_hidden_state [batch, tokens, 1024] as output 0 — the shape the
		// Cls path pools. Exports that emit a pre-pooled dense_vecs [batch, 1024] instead
		// (e.g. gpahal/bge-m3-onnx-int8) do NOT run through this path.
		return OnnxModel.FromFiles(
			Name,
			Path.Combine(directory, ModelFile),
			new Dictionary<string, string> { [TokenizerFile] = Path.Combine(directory, TokenizerFile) });
	}
}
