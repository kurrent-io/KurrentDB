// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Embeddings;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Testing;

namespace Benchmarks;

/// <summary>
/// snowflake-arctic-embed-l-v2.0 — an XLM-R backbone trained for retrieval rather than paraphrase
/// similarity, which is the task Kontext actually performs. 1024 dimensions, CLS pooling, and its own
/// repo publishes the SentencePiece model beside the ONNX.
/// </summary>
/// <remarks>
/// Asymmetric like the e5 family, but the other way around: queries carry <c>"query: "</c> and stored
/// text carries nothing. Verified against the model card and 1_Pooling/config.json
/// (pooling_mode_cls_token = true) rather than assumed from the family.
/// </remarks>
public sealed class ArcticEmbeddingGenerator : SentencePieceOnnxEmbeddingGenerator {
	const string Name          = "snowflake-arctic-embed-l-v2.0";
	const string TokenizerFile = "sentencepiece.bpe.model";
	const string RepoUrl       = "https://huggingface.co/Snowflake/snowflake-arctic-embed-l-v2.0/resolve/main";

	public ArcticEmbeddingGenerator(Action<SentencePieceOnnxOptions>? configure = null)
		: base(Model(), Options(configure)) { }

	static SentencePieceOnnxOptions Options(Action<SentencePieceOnnxOptions>? configure) {
		var options = new SentencePieceOnnxOptions {
			PoolingMode    = EmbeddingPoolingMode.Cls,
			MaxTokens      = 512,
			QueryPrefix    = "query: ",
			DocumentPrefix = null,
		};

		configure?.Invoke(options);

		return options;
	}

	static OnnxModel Model() {
		// model_quantized, not model.onnx: the fp32 export is split across model.onnx + model.onnx_data
		// (ONNX external data), which OnnxModel.FromFiles cannot open as a single file.
		const string ModelFile = "model_quantized.onnx";

		var directory = ModelCache.Ensure(BenchmarkModels.Directory("arctic"), [
			($"{RepoUrl}/onnx/{ModelFile}", ModelFile),
			($"{RepoUrl}/{TokenizerFile}", TokenizerFile),
		]);

		return OnnxModel.FromFiles(
			Name,
			Path.Combine(directory, ModelFile),
			new Dictionary<string, string> { [TokenizerFile] = Path.Combine(directory, TokenizerFile) });
	}
}
