// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Retrieval;
using TUnit.Core.Interfaces;
using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;

namespace Kurrent.Kontext.Testing;

/// <summary>
/// A real long-form conversation seeded into a <see cref="KontextStoreFixture"/>, alongside that
/// conversation's questions and per-question ground truth.
///
/// Shared PerTestSession: the cost is all up-front (419 sequential ONNX embeds, one schema build)
/// and nothing a test does mutates it.
/// </summary>
public sealed class KontextCorpus : IAsyncInitializer, IAsyncDisposable {
	const string CorpusFile = "locomo-conv26.json";

	readonly KontextStoreFixture _store = new();

	/// <summary>The committed corpus as loaded: memories, questions and ground truth.</summary>
	public CorpusFixture Data { get; private set; } = null!;

	public KontextDataStore Store => _store.Store;

	/// <summary>The model that embedded the corpus — the vector leg must query with the same one.</summary>
	public EmbeddingGenerator EmbeddingGenerator => _store.EmbeddingGenerator;

	public IReadOnlyList<CorpusQuestion> Questions => Data.Questions;

	public int MemoryCount => Data.Memories.Count;

	public async Task InitializeAsync() {
		Data = await CorpusFixture.Load(Path.Combine(AppContext.BaseDirectory, "Corpus", "Data", CorpusFile));

		await _store.InitializeAsync();

		await _store.SeedEmbedded([
			.. Data.Memories.Select(memory => new MemoryRow(
				Id: memory.Id,
				Type: Contracts.MemoryType.Observation,
				Content: memory.Content,
				Importance: Contracts.MemoryImportance.Normal,
				RetainedAt: memory.RetainedAt)),
		]);
	}

	/// <summary>Runs every question through a pipeline and pairs what came back with what should have.</summary>
	public async ValueTask<IReadOnlyList<RankedOutcome>> Evaluate(IKontextRetriever retriever, int limit = 10, CancellationToken ct = default) {
		var outcomes = new List<RankedOutcome>(Questions.Count);

		foreach (var question in Questions) {
			// MinScore stays 0: its scale is pipeline-dependent, so any cutoff would cut different
			// amounts from the compositions being compared and invalidate the comparison.
			var ranked = await retriever.RetrieveAsync(new() {
				Text  = question.Question,
				Limit = limit,
				AsOf  = Data.AsOf,
			}, ct);

			outcomes.Add(new([.. ranked.Select(scored => scored.Memory.MemoryId)], question.Relevant.ToHashSet()));
		}

		return outcomes;
	}

	public ValueTask DisposeAsync() => _store.DisposeAsync();
}
