// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Embeddings;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNext.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KurrentDB.Kontext.Search;

/// <summary>
/// Cross-encoder re-ranking using ms-marco-TinyBERT-L2-v2 via ONNX Runtime.
/// </summary>
public class CrossEncoderService(ModelManager modelManager, ILogger<CrossEncoderService> logger) : IReranker, IDisposable {
	// Per ms-marco-TinyBERT-L2-v2's max_position_embeddings.
	const int MaxModelTokens = 512;

	InferenceSession? _session;

	const int TokenizerPoolCapacity = 32;
	readonly WordPieceTokenizer?[] _tokenizers = new WordPieceTokenizer?[TokenizerPoolCapacity];
	readonly IndexPool _tokenizerSlots = new(TokenizerPoolCapacity);

	public Task InitializeAsync() {
		_session = new InferenceSession(modelManager.CrossEncoderModel);
		logger.LogInformation("Cross-encoder model loaded");
		return Task.CompletedTask;
	}

	WordPieceTokenizer NewTokenizer() => new(modelManager.CrossEncoderVocab, MaxModelTokens);

	(WordPieceTokenizer Tokenizer, int Slot) RentTokenizer() {
		if (_tokenizerSlots.TryGet(out var slot))
			return (_tokenizers[slot] ??= NewTokenizer(), slot);
		return (NewTokenizer(), -1);
	}

	void ReturnTokenizer(int slot) {
		if (slot >= 0)
			_tokenizerSlots.Return(slot);
	}

	public async IAsyncEnumerable<ScoredDoc> RerankAsync(
		string query,
		IReadOnlyList<(ulong DocId, string Document)> candidates,
		[EnumeratorCancellation] CancellationToken cancellationToken = default) {
		if (_session == null)
			throw new InvalidOperationException($"{nameof(CrossEncoderService)} not initialized");

		var (tokenizer, slot) = RentTokenizer();
		List<ScoredDoc> scored;
		try {
			scored = new List<ScoredDoc>(candidates.Count);

			foreach (var (id, document) in candidates) {
				cancellationToken.ThrowIfCancellationRequested();

				var (inputIds, attentionMask, tokenTypeIds) = tokenizer.EncodePair(query, document);
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
				var logits = results[0].AsTensor<float>();
				scored.Add(new(id, logits[0, 0]));
			}
		} finally {
			ReturnTokenizer(slot);
		}

		scored.Sort((a, b) => b.Score.CompareTo(a.Score));
		foreach (var s in scored)
			yield return s;
		await Task.CompletedTask;
	}

	public void Dispose() {
		_session?.Dispose();
	}
}