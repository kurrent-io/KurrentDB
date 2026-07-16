// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;

namespace Kurrent.Kontext.Embeddings.PrototypeV2;

/// <summary>
/// The DEFAULT local embedding generator (implementation A): the hand-rolled WordPiece tokenizer on
/// all-MiniLM-L6-v2, ported verbatim from <c>KurrentDB.Kontext</c>. Byte-faithful to the vectors already
/// stored in existing indexes — its behaviour must not change. Implementation B
/// (<see cref="Kurrent.Kontext.Embeddings.WordPieceOnnx.WordPieceOnnxEmbeddingGenerator"/>) is an opt-in upgrade
/// that fixes accent handling but produces different vectors, so it is deliberately not the default.
/// </summary>
public sealed class PrototypeV2EmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>> {
	const string ProviderName = "kontext-prototype-v2";
	const string DefaultModelId = "all-MiniLM-L6-v2";

	readonly InferenceSession _session;
	readonly HandRolledWordPieceTokenizer _tokenizer;
	readonly EmbeddingPoolingMode _poolingMode;
	readonly bool _normalizeEmbeddings;
	readonly bool _feedTokenTypeIds;
	readonly EmbeddingGeneratorMetadata _metadata;

	/// <summary>
	/// Builds the default generator from ONNX model and vocab streams. Both streams are consumed in full
	/// during construction; the caller owns closing them afterwards.
	/// </summary>
	public PrototypeV2EmbeddingGenerator(Stream onnxModel, Stream vocab, PrototypeV2Options? options = null) {
		var resolved = options ?? new PrototypeV2Options();
		_tokenizer = HandRolledWordPieceTokenizer.Create(vocab, resolved.MaxTokens);
		_session = OnnxModelLoader.CreateSession(OnnxModelLoader.ReadAllBytes(onnxModel));
		_poolingMode = resolved.PoolingMode;
		_normalizeEmbeddings = resolved.NormalizeEmbeddings;
		_feedTokenTypeIds = _session.InputMetadata.ContainsKey("token_type_ids");

		// Warm-up run so the embedding dimension is read from the actual ONNX output rather than guessed.
		var probe = _session.Embed(_tokenizer.Encode("probe"), _feedTokenTypeIds, _poolingMode, _normalizeEmbeddings);
		_metadata = new EmbeddingGeneratorMetadata(
			providerName: ProviderName,
			defaultModelId: resolved.ModelId ?? DefaultModelId,
			defaultModelDimensions: probe.Length);
	}

	public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
		IEnumerable<string> values,
		EmbeddingGenerationOptions? options = null,
		CancellationToken cancellationToken = default) {
		var results = new GeneratedEmbeddings<Embedding<float>>();
		foreach (var text in values) {
			cancellationToken.ThrowIfCancellationRequested();
			var vector = _session.Embed(_tokenizer.Encode(text), _feedTokenTypeIds, _poolingMode, _normalizeEmbeddings);
			results.Add(new Embedding<float>(vector));
		}
		return Task.FromResult(results);
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
