// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;

namespace Kurrent.Kontext.Embeddings.WordPieceOnnx;

/// <summary>
/// A BERT/WordPiece ONNX embedding generator (implementation B). Holds a Microsoft.ML.Tokenizers
/// <see cref="BertTokenizer"/> and an <see cref="InferenceSession"/>, and turns text into vectors via the
/// shared <c>session.Embed(…)</c>. Runs any BERT/WordPiece ONNX model; validated on all-MiniLM-L6-v2:
/// bit-identical to the hand-rolled path on ASCII, and it fixes the accent bug (café ≡ cafe).
/// </summary>
public sealed class WordPieceOnnxEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>> {
	const string ProviderName = "kontext-wordpiece-onnx";
	const string DefaultModelId = "all-MiniLM-L6-v2";

	const string IcuHint =
		"BERT accent-folding relies on String.Normalize(FormD), which is a silent no-op under " +
		"InvariantGlobalization (no ICU). Run with ICU present (InvariantGlobalization=false; app-local ICU " +
		"for Native AOT) so 'café' folds to 'cafe' instead of fragmenting into [UNK].";

	readonly InferenceSession _session;
	readonly BertTokenizer _tokenizer;
	readonly int _maxTokens;
	readonly EmbeddingPoolingMode _poolingMode;
	readonly bool _normalizeEmbeddings;
	readonly bool _feedTokenTypeIds;
	readonly EmbeddingGeneratorMetadata _metadata;

	/// <summary>
	/// Resolves the model named by <see cref="WordPieceOnnxOptions.ModelId"/> (defaulting to
	/// <c>all-MiniLM-L6-v2</c>) from <paramref name="registry"/> and builds the generator. The main entry
	/// point; resolution is cheap and nothing is read until this constructor runs, so no model bytes move at
	/// DI-registration time.
	/// </summary>
	public WordPieceOnnxEmbeddingGenerator(OnnxModelRegistry registry, WordPieceOnnxOptions? options = null)
		: this(registry.Get((options ?? new WordPieceOnnxOptions()).ModelId ?? DefaultModelId), options) { }

	/// <summary>
	/// Builds the generator directly from a resolved <see cref="OnnxModel"/> (handy for tests). Reads the ONNX
	/// model and the WordPiece vocab asset from it; the streams the model opens are consumed and closed here.
	/// Fails loudly at construction if ICU accent-folding is unavailable (see the ICU gate).
	/// </summary>
	public WordPieceOnnxEmbeddingGenerator(OnnxModel model, WordPieceOnnxOptions? options = null) {
		var resolved = options ?? new WordPieceOnnxOptions();

		// Uncased BERT preprocessing is intrinsic to implementation B: always lower-case + strip accents
		// (café -> cafe) — that IS the fix, not an opt-in — plus per-character CJK splitting.
		using (var vocab = model.ReadAsset(resolved.VocabAsset))
			_tokenizer = BertTokenizer.Create(vocab, new BertOptions {
				LowerCaseBeforeTokenization = true,
				RemoveNonSpacingMarks = true,
				IndividuallyTokenizeCjk = true,
				ApplyBasicTokenization = true,
			});
		GateIcu(_tokenizer);
		_maxTokens = resolved.MaxTokens;

		using (var onnx = model.ReadModel())
			_session = OnnxModelLoader.CreateSession(OnnxModelLoader.ReadAllBytes(onnx));
		_poolingMode = resolved.PoolingMode;
		_normalizeEmbeddings = resolved.NormalizeEmbeddings;
		_feedTokenTypeIds = _session.InputMetadata.ContainsKey("token_type_ids");

		// Warm-up run so the embedding dimension is read from the actual ONNX output rather than guessed.
		var probe = _session.Embed(Encode("probe"), _feedTokenTypeIds, _poolingMode, _normalizeEmbeddings);
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
			var vector = _session.Embed(Encode(text), _feedTokenTypeIds, _poolingMode, _normalizeEmbeddings);
			results.Add(new Embedding<float>(vector));
		}
		return Task.FromResult(results);
	}

	// Encodes with [CLS] … [SEP] (BertTokenizer adds them), an all-ones attention mask, and no token type
	// ids (single-sentence embedding is always segment 0 — inference zero-fills when the model declares it).
	EncodedInput Encode(string text) {
		var ids = _tokenizer.EncodeToIds(text, _maxTokens, out _, out _);

		var inputIds = new long[ids.Count];
		var attentionMask = new long[ids.Count];
		for (var i = 0; i < ids.Count; i++) {
			inputIds[i] = ids[i];
			attentionMask[i] = 1;
		}

		return new EncodedInput(inputIds, attentionMask, TokenTypeIds: null);
	}

	// Fail loud if accent-folding is silently disabled (no ICU). Verified against the real tokenizer: with
	// folding active, "café" and "cafe" encode to the same ids; without ICU they diverge.
	static void GateIcu(BertTokenizer tokenizer) {
		IReadOnlyList<int> accented, plain;
		try {
			accented = tokenizer.EncodeToIds("café");
			plain = tokenizer.EncodeToIds("cafe");
		} catch (Exception ex) {
			throw new InvalidOperationException($"ICU is not active (tokenization threw). {IcuHint}", ex);
		}

		if (!accented.SequenceEqual(plain))
			throw new InvalidOperationException(
				$"ICU accent-folding check FAILED: 'café' and 'cafe' tokenized differently. {IcuHint}");
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
