// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;

namespace Kurrent.Kontext.Embeddings.SentencePieceOnnx;

/// <summary>
/// A SentencePiece / XLM-R ONNX embedding generator (implementation C) — the multilingual showcase. Holds a
/// Microsoft.ML.Tokenizers <see cref="SentencePieceTokenizer"/>, applies the XLM-R fairseq id remap, and runs
/// the shared <c>session.Embed(…)</c>. Handles any XLM-RoBERTa-tokenized ONNX model (multilingual-e5,
/// paraphrase-multilingual, bge-m3); its assets come from an <see cref="OnnxModel"/> and the input prefix +
/// pooling mode from <see cref="SentencePieceOnnxOptions"/>. Verified bit-exact against a transformers.js
/// reference for multilingual-e5-small.
/// </summary>
public sealed class SentencePieceOnnxEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>> {
	const string ProviderName = "kontext-sentencepiece-onnx";
	const string DefaultModelId = "multilingual-e5-small";

	// XLM-R / fairseq special-token ids (see HuggingFace XLMRobertaTokenizer's id layout).
	const int Bos = 0;          // <s>
	const int Eos = 2;          // </s>
	const int FairseqUnk = 3;   // <unk>
	const int FairseqOffset = 1;

	readonly SentencePieceOnnxOptions _options;
	readonly InferenceSession _session;
	readonly SentencePieceTokenizer _sp;
	readonly int _spUnknownId;
	readonly bool _feedTokenTypeIds;
	readonly EmbeddingGeneratorMetadata _metadata;

	/// <summary>
	/// Resolves the model named by <see cref="SentencePieceOnnxOptions.ModelId"/> (defaulting to
	/// <c>multilingual-e5-small</c>) from <paramref name="registry"/> and builds the generator. The main entry
	/// point; resolution is cheap and nothing is read until this constructor runs, so no model bytes move at
	/// DI-registration time.
	/// </summary>
	public SentencePieceOnnxEmbeddingGenerator(OnnxModelRegistry registry, SentencePieceOnnxOptions? options = null)
		: this(registry.Get((options ?? new SentencePieceOnnxOptions()).ModelId ?? DefaultModelId), options) { }

	/// <summary>
	/// Builds the generator directly from a resolved <see cref="OnnxModel"/> (handy for tests). Reads the ONNX
	/// model and the SentencePiece asset from it; the streams the model opens are consumed and closed here.
	/// </summary>
	public SentencePieceOnnxEmbeddingGenerator(OnnxModel model, SentencePieceOnnxOptions? options = null) {
		_options = options ?? new SentencePieceOnnxOptions();

		using (var spm = model.ReadAsset(_options.TokenizerAsset))
			_sp = SentencePieceTokenizer.Create(spm, addBeginningOfSentence: false, addEndOfSentence: false);
		_spUnknownId = _sp.UnknownId;

		using (var onnx = model.ReadModel())
			_session = OnnxModelLoader.CreateSession(OnnxModelLoader.ReadAllBytes(onnx));
		_feedTokenTypeIds = _session.InputMetadata.ContainsKey("token_type_ids");

		// Warm-up run so the embedding dimension is read from the actual ONNX output rather than guessed.
		var probe = _session.Embed(Encode("probe"), _feedTokenTypeIds, _options.PoolingMode, _options.NormalizeEmbeddings);
		_metadata = new EmbeddingGeneratorMetadata(
			providerName: ProviderName,
			defaultModelId: model.Name,
			defaultModelDimensions: probe.Length);
	}

	public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
		IEnumerable<string> values,
		EmbeddingGenerationOptions? options = null,
		CancellationToken cancellationToken = default) {
		var results = new GeneratedEmbeddings<Embedding<float>>();
		foreach (var text in values) {
			cancellationToken.ThrowIfCancellationRequested();
			var vector = _session.Embed(Encode(text), _feedTokenTypeIds, _options.PoolingMode, _options.NormalizeEmbeddings);
			results.Add(new Embedding<float>(vector));
		}
		return Task.FromResult(results);
	}

	// Applies the fairseq id remap XLM-R ONNX models expect: every raw SentencePiece id shifts by +1, the
	// unknown id maps to fairseq <unk> = 3, and the sequence is wrapped <s> (0) … </s> (2). Single-segment
	// input, so no token type ids. Verified bit-exact against transformers.js for multilingual-e5-small.
	EncodedInput Encode(string text) {
		var input = _options.InputPrefix is null ? text : _options.InputPrefix + text;
		var raw = _sp.EncodeToIds(input, addBeginningOfSentence: false, addEndOfSentence: false);

		// Reserve two slots for <s> … </s> and truncate the body to fit MaxTokens.
		var bodyLen = Math.Min(raw.Count, _options.MaxTokens - 2);

		var inputIds = new long[bodyLen + 2];
		inputIds[0] = Bos;
		for (var i = 0; i < bodyLen; i++) {
			var id = raw[i];
			inputIds[i + 1] = id == _spUnknownId ? FairseqUnk : id + FairseqOffset;
		}
		inputIds[^1] = Eos;

		var attentionMask = new long[inputIds.Length];
		Array.Fill(attentionMask, 1L);

		return new EncodedInput(inputIds, attentionMask, TokenTypeIds: null);
	}

	public object? GetService(Type serviceType, object? serviceKey = null) {
		if (serviceKey is not null)
			return null;
		if (serviceType == typeof(EmbeddingGeneratorMetadata))
			return _metadata;
		return serviceType.IsInstanceOfType(this) ? this : null;
	}

	public void Dispose() => _session.Dispose();
}
