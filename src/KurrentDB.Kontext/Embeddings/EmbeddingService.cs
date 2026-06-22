// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;
using DotNext.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KurrentDB.Kontext.Embeddings;

/// <summary>
/// Generates 384-dimensional sentence embeddings using all-MiniLM-L6-v2 via ONNX Runtime.
/// </summary>
public class EmbeddingService(ModelManager modelManager, ILogger<EmbeddingService> logger) : IEmbeddingGenerator<string, Embedding<float>>,
	IDisposable {
	public const int Dimensions = 384;

	// Per all-MiniLM-L6-v2's max_position_embeddings.
	const int MaxModelTokens = 512;

	InferenceSession? _session;

	const int TokenizerPoolCapacity = 64;
	readonly WordPieceTokenizer?[] _tokenizers = new WordPieceTokenizer?[TokenizerPoolCapacity];
	readonly IndexPool _tokenizerSlots = new(TokenizerPoolCapacity);

	readonly EmbeddingGeneratorMetadata _metadata = new(
		providerName: "local",
		defaultModelId: "all-MiniLM-L6-v2",
		defaultModelDimensions: Dimensions);

	public Task InitializeAsync() {
		var opts = new SessionOptions {
			ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
			IntraOpNumThreads = Math.Min(Math.Max(4, Environment.ProcessorCount / 4), Environment.ProcessorCount),
		};
		_session = new InferenceSession(modelManager.EmbeddingModel, opts);
		logger.LogInformation("Embedding model loaded");
		return Task.CompletedTask;
	}

	WordPieceTokenizer NewTokenizer() => new(modelManager.EmbeddingVocab, MaxModelTokens);

	(WordPieceTokenizer Tokenizer, int Slot) RentTokenizer() {
		if (_tokenizerSlots.TryGet(out var slot))
			return (_tokenizers[slot] ??= NewTokenizer(), slot);
		return (NewTokenizer(), -1);
	}

	void ReturnTokenizer(int slot) {
		if (slot >= 0)
			_tokenizerSlots.Return(slot);
	}

	public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
		IEnumerable<string> values,
		EmbeddingGenerationOptions? options = null,
		CancellationToken cancellationToken = default) {
		var results = new GeneratedEmbeddings<Embedding<float>>();
		foreach (var text in values) {
			cancellationToken.ThrowIfCancellationRequested();
			results.Add(new Embedding<float>(GetEmbedding(text)));
		}
		return Task.FromResult(results);
	}

	public object? GetService(Type serviceType, object? serviceKey = null) {
		if (serviceKey is not null)
			return null;
		if (serviceType == typeof(EmbeddingGeneratorMetadata))
			return _metadata;
		if (serviceType.IsInstanceOfType(this))
			return this;
		return null;
	}

	internal float[] GetEmbedding(string text) {
		if (_session == null)
			throw new InvalidOperationException($"{nameof(EmbeddingService)} not initialized");

		var (tokenizer, slot) = RentTokenizer();
		try {
			var (inputIds, attentionMask, tokenTypeIds) = tokenizer.Encode(text);
			var seqLen = inputIds.Length;

			var inputs = new List<NamedOnnxValue>
			{
				NamedOnnxValue.CreateFromTensor("input_ids",
					new DenseTensor<long>(MemoryMarshal.AsMemory(inputIds), [1, seqLen])),
				NamedOnnxValue.CreateFromTensor("attention_mask",
					new DenseTensor<long>(MemoryMarshal.AsMemory(attentionMask), [1, seqLen])),
				NamedOnnxValue.CreateFromTensor("token_type_ids",
					new DenseTensor<long>(MemoryMarshal.AsMemory(tokenTypeIds), [1, seqLen])),
			};

			using var results = _session.Run(inputs);

			// Output: last_hidden_state [1, seq_len, 384]
			var output = results[0].AsTensor<float>();

			// Mean pooling with attention mask
			var embedding = new float[Dimensions];
			float maskSum = 0;
			for (var i = 0; i < seqLen; i++) {
				if (attentionMask.Span[i] == 0)
					continue;
				maskSum++;
				for (var j = 0; j < Dimensions; j++)
					embedding[j] += output[0, i, j];
			}

			if (maskSum > 0) {
				for (var j = 0; j < Dimensions; j++)
					embedding[j] /= maskSum;
			}

			// L2 normalize
			var norm = MathF.Sqrt(embedding.Sum(x => x * x));
			if (norm > 0) {
				for (var j = 0; j < Dimensions; j++)
					embedding[j] /= norm;
			}

			return embedding;
		} finally {
			ReturnTokenizer(slot);
		}
	}

	public void Dispose() {
		_session?.Dispose();
	}
}
