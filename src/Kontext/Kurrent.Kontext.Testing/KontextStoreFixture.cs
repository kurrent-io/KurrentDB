// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Infrastructure.Data;
using Microsoft.Extensions.AI;
using TUnit.Core.Interfaces;
using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;

namespace Kurrent.Kontext.Testing;

/// <summary>
/// A REAL DuckDB + Lance store with the REAL pMM12 embedding model behind it: owns the temp directory,
/// the connection pool, the schema and the generator, and is the one place that embeds content before
/// seeding — every suite and benchmark that ranks on real vectors goes through it.
/// </summary>
/// <remarks>
/// Inject with <c>[ClassDataSource&lt;KontextStoreFixture&gt;(Shared = SharedType.None)]</c> for a
/// fresh store per test, or create and initialize directly outside a test host.
/// </remarks>
public sealed class KontextStoreFixture : IAsyncInitializer, IAsyncDisposable {
	readonly SentencePieceOnnxEmbeddingGenerator _embeddingGenerator = InterimPmm12.CreateEmbeddingGenerator();

	TempDir?               _dir;
	KontextConnectionPool? _pool;

	public KontextDataStore Store { get; private set; } = null!;

	/// <summary>The model the seeds were embedded with — the vector leg must query with the same one.</summary>
	public EmbeddingGenerator EmbeddingGenerator => _embeddingGenerator;

	public async Task InitializeAsync() {
		_dir  = new TempDir();
		_pool = MemorySeeding.NewPool(_dir.Path);

		await MemorySeeding.CreateSchema(_pool, Dimension(_embeddingGenerator));

		Store = new(_pool);
	}

	/// <summary>
	/// Inserts the rows with embeddings the model computed from their content, as the projector
	/// will. Whatever <see cref="MemoryRow.Embedding"/> a row carries is replaced.
	/// </summary>
	public async ValueTask SeedEmbedded(params MemoryRow[] rows) {
		var embeddings = await _embeddingGenerator.GenerateAsync(rows.Select(row => row.Content).ToList());

		MemorySeeding.Insert(
			_pool ?? throw new InvalidOperationException("The fixture is not initialized."),
			[.. rows.Zip(embeddings, (row, embedding) => row with { Embedding = embedding.Vector.ToArray() })]);
	}

	/// <summary>Read from the model's real output by the generator's warm-up probe, never hard-coded.</summary>
	static int Dimension(EmbeddingGenerator generator) =>
		generator.GetService(typeof(EmbeddingGeneratorMetadata)) is EmbeddingGeneratorMetadata { DefaultModelDimensions: { } dimension }
			? dimension
			: throw new InvalidOperationException("The embedding generator does not report a dimension, so the schema cannot be sized.");

	public ValueTask DisposeAsync() {
		// The directory can only go once nothing holds a handle into the engine files.
		_pool?.Dispose();
		_embeddingGenerator.Dispose();
		_dir?.Dispose();

		return ValueTask.CompletedTask;
	}
}
