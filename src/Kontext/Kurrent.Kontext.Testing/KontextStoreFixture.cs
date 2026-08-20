// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings;
using Kurrent.Kontext.Embeddings.SentencePieceOnnx;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Memory.Data;
using Microsoft.Extensions.AI;
using TUnit.Core.Interfaces;
using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;

namespace Kurrent.Kontext.Testing;

/// <summary>
/// Builds the generator a fixture embeds with, applying the caller's option tweaks on top of the
/// model's own defaults. Shaped to match the model-specific generators' constructors, so a factory
/// passes as a lambda.
/// </summary>
public delegate SentencePieceOnnxEmbeddingGenerator EmbeddingModelFactory(Action<SentencePieceOnnxOptions>? configure);

/// <summary>
/// A REAL DuckDB + Lance store with a REAL embedding model behind it: owns the temp directory,
/// the data sources, the schema and the generator, and is the one place that embeds content before
/// seeding — every suite and benchmark that ranks on real vectors goes through it.
/// </summary>
/// <remarks>
/// Inject with <c>[ClassDataSource&lt;KontextStoreFixture&gt;(Shared = SharedType.None)]</c> for a
/// fresh store per test, or create and initialize directly outside a test host.
/// </remarks>
public sealed class KontextStoreFixture(
	Action<SentencePieceOnnxOptions>? embeddingOptions,
	EmbeddingModelFactory? embeddingModel = null
) : IAsyncInitializer, IAsyncDisposable {
	// ClassDataSource<T> requires a true parameterless constructor — an optional parameter
	// does not satisfy the TUnit analyzer.
	public KontextStoreFixture() : this(null) { }

	readonly SentencePieceOnnxEmbeddingGenerator _embeddingGenerator =
		embeddingModel is null
			? new Pmm12EmbeddingGenerator(embeddingOptions)
			: embeddingModel(embeddingOptions);

	TempDir?            _dir;
	KontextDataSource? _dataSources;

	public KontextMemoryDataStore Store { get; private set; } = null!;

	/// <summary>The engine door — benchmarks use it to reshape indexes between phases.</summary>
	public KontextDataSource DataSources => _dataSources ?? throw new InvalidOperationException("The fixture is not initialized.");

	/// <summary>The model the seeds were embedded with — the vector leg must query with the same one.</summary>
	public EmbeddingGenerator EmbeddingGenerator => _embeddingGenerator;

	public async Task InitializeAsync() {
		_dir         = new TempDir();
		_dataSources = MemorySeeding.NewDataSources(_dir.Path);

		await MemorySeeding.CreateSchema(_dataSources);

		Store = new(_dataSources);
	}

	/// <summary>
	/// Inserts the rows with embeddings the model computed from their content, as the projector
	/// will. Whatever <see cref="MemoryRow.Embedding"/> a row carries is replaced.
	/// </summary>
	public async ValueTask SeedEmbedded(params MemoryRow[] rows) {
		var embeddings = await _embeddingGenerator.GenerateAsync(rows.Select(row => row.Content).ToList());

		MemorySeeding.Insert(
			_dataSources ?? throw new InvalidOperationException("The fixture is not initialized."),
			[.. rows.Zip(embeddings, (row, embedding) => row with { Embedding = embedding.Vector.ToArray() })]);
	}

	public ValueTask DisposeAsync() {
		// The directory can only go once nothing holds a handle into the engine files.
		_dataSources?.Dispose();
		_embeddingGenerator.Dispose();
		_dir?.Dispose();

		return ValueTask.CompletedTask;
	}
}
