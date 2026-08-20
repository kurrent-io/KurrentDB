// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Memory.Data;
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
public sealed class KontextCorpus(
	Action<SentencePieceOnnxOptions>? embeddingOptions,
	EmbeddingModelFactory? embeddingModel = null
) : IAsyncInitializer, IAsyncDisposable {
	const string CorpusFile = "locomo-conv26.json";

	// ClassDataSource<T> requires a true parameterless constructor — an optional parameter
	// does not satisfy the TUnit analyzer.
	public KontextCorpus() : this(null) { }

	readonly KontextStoreFixture _store = new(embeddingOptions, embeddingModel);

	/// <summary>The committed corpus as loaded: memories, questions and ground truth.</summary>
	public CorpusFixture Data { get; private set; } = null!;

	public KontextMemoryDataStore Store => _store.Store;

	/// <summary>The engine door for index experiments.</summary>
	public KontextDataSource DataSources => _store.DataSources;

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

		// The schema creates content_fts on the empty table, so every seeded row lands in the
		// unindexed tail — where lance_fts returns the FIRST k rows by scan arrival, not the top
		// k by score (probed 2026-08-16: a top-scoring needle vanishes, membership varies per
		// scan). Rebuilding after the seed puts every ranking measurement on the real index.
		RebuildContentFts();

		void RebuildContentFts() =>
			DataSources.Execute(connection => {
				using var command = connection.CreateCommand();
				command.CommandText =
					"""
					CREATE INDEX content_fts ON ldb.main.memories (content) USING INVERTED
					WITH (replace = true, base_tokenizer = 'simple', language = 'English', stem = true);
					""";
				command.ExecuteNonQuery();
			});
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
