// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Modules.Entities;
using Kurrent.Kontext.Testing;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Tests;

/// <summary>
/// Behavioural tests for <see cref="KontextEntityResolver"/> against a REAL DuckDB + Lance
/// engine. The resolver only reads, so each test seeds the entities table directly with SQL —
/// exactly how the projector will write it. No embedding model anywhere: alias embeddings are
/// hand-computable axis vectors zero-padded to the schema dimension, and semantic queries pass
/// the span embedding in.
/// </summary>
[Category("Integration")]
public class KontextEntityResolverTests {
	const double Tolerance = 0.001;

	[Test]
	public async ValueTask exact_resolution_matches_same_type_aliases_only() {
		// Arrange
		using var dir        = new TempDir();
		using var dataSource = await Seed(dir.Path);

		var resolver = new KontextEntityResolver(dataSource, new NoEmbeddings());

		// Act
		var resolved = await resolver.ResolveExactAsync([
			new EntityKey("organization", "acme corp"),
			new EntityKey("person", "acme corp"),
		]);

		// Assert
		await Assert.That(resolved).Count().IsEqualTo(1);
		await Assert.That(resolved[new EntityKey("organization", "acme corp")]).IsEqualTo("e-acme");
	}

	[Test]
	public async ValueTask semantic_resolution_scores_an_identical_alias_at_one() {
		// Arrange
		using var dir        = new TempDir();
		using var dataSource = await Seed(dir.Path);

		var resolver = new KontextEntityResolver(dataSource, new NoEmbeddings());
		var key      = new EntityKey("organization", "acme corp");

		// Act — the span's embedding sits exactly on the stored alias vector and the normalized
		// text equals the alias, so both legs score 1 and the combined score is exactly 1.
		var resolved = await resolver.ResolveSemanticAsync([new SemanticQuery(key, Embed(1f, 0f, 0f, 0f))]);

		// Assert
		await Assert.That(resolved[key].EntityId).IsEqualTo("e-acme");
		await Assert.That(resolved[key].Confidence).IsEqualTo(1.0).Within(Tolerance);
	}

	[Test]
	public async ValueTask semantic_resolution_never_crosses_entity_types() {
		// Arrange
		using var dir        = new TempDir();
		using var dataSource = await Seed(dir.Path);

		var resolver = new KontextEntityResolver(dataSource, new NoEmbeddings());
		var key      = new EntityKey("person", "acme corp");

		// Act — the vector sits exactly on an organization alias, but the span is a person.
		var resolved = await resolver.ResolveSemanticAsync([new SemanticQuery(key, Embed(1f, 0f, 0f, 0f))]);

		// Assert
		await Assert.That(resolved.ContainsKey(key)).IsFalse();
	}

	[Test]
	public async ValueTask semantic_resolution_fuzzy_corroboration_outranks_raw_vector_proximity() {
		// Arrange
		using var dir        = new TempDir();
		using var dataSource = await Seed(dir.Path);

		var resolver = new KontextEntityResolver(dataSource, new NoEmbeddings());
		var key      = new EntityKey("organization", "acme corp");

		// Act — the span's vector is closer to Zenith (cos 0.7) than to Acme (cos 0.6), but the
		// name is identical to Acme's alias: (0.6 + 1.0) / 2 = 0.8 beats Zenith's lone 0.7.
		var resolved = await resolver.ResolveSemanticAsync([
			new SemanticQuery(key, Embed(0.6f, 0.7f, 0f, 0.38729833f)),
		]);

		// Assert
		await Assert.That(resolved[key].EntityId).IsEqualTo("e-acme");
		await Assert.That(resolved[key].Confidence).IsEqualTo(0.8).Within(0.01);
	}

	[Test]
	public async ValueTask semantic_resolution_reports_the_raw_vector_score_when_no_name_corroborates() {
		// Arrange
		using var dir        = new TempDir();
		using var dataSource = await Seed(dir.Path);

		var resolver = new KontextEntityResolver(dataSource, new NoEmbeddings());
		var key      = new EntityKey("organization", "globex holdings");

		// Act — nothing in the catalog resembles the name, so the nearest same-type vector wins
		// with its similarity unboosted.
		var resolved = await resolver.ResolveSemanticAsync([
			new SemanticQuery(key, Embed(0f, 0.9f, 0.43588989f, 0f)),
		]);

		// Assert
		await Assert.That(resolved[key].EntityId).IsEqualTo("e-zenith");
		await Assert.That(resolved[key].Confidence).IsEqualTo(0.9).Within(0.01);
	}

	[Test]
	public async ValueTask semantic_resolution_returns_nothing_from_an_empty_catalog() {
		// Arrange
		using var dir        = new TempDir();
		using var dataSource = MemorySeeding.NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSource);

		var resolver = new KontextEntityResolver(dataSource, new NoEmbeddings());
		var key      = new EntityKey("organization", "acme corp");

		// Act
		var resolved = await resolver.ResolveSemanticAsync([new SemanticQuery(key, Embed(1f, 0f, 0f, 0f))]);

		// Assert
		await Assert.That(resolved).IsEmpty();
	}

	#region ->> Test Infrastructure <<-

	/// <summary>The primitives under test take embeddings as input, so the resolver never embeds here.</summary>
	sealed class NoEmbeddings : IEmbeddingGenerator<string, Embedding<float>> {
		public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
			IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default
		) => throw new NotSupportedException("These tests never embed.");

		public object? GetService(Type serviceType, object? serviceKey = null) => null;

		public void Dispose() { }
	}

	/// <summary>
	/// Pads a small hand-computable vector to the schema dimension: the zero tail contributes
	/// nothing to any cosine, so every asserted score stays a figure a human can recompute.
	/// </summary>
	static float[] Embed(params float[] head) {
		var padded = new float[KontextSchemaTask.Dimension];
		head.CopyTo(padded, 0);
		return padded;
	}

	/// <summary>
	/// Creates the schema through the migration step and seeds three aliases on orthogonal axes,
	/// so every semantic score in the tests is an exact cosine a human can recompute.
	/// </summary>
	static async ValueTask<KontextDataSource> Seed(string dir) {
		var dataSource = MemorySeeding.NewDataSources(dir);

		await MemorySeeding.CreateSchema(dataSource);

		// Parameter-bound like the projector writes: a literal CAST(... AS FLOAT[4]) refuses the
		// FLOAT[384] column, a bound float[] lands as the array it is.
		const string sql =
			"""
			INSERT INTO ldb.main.entities (entity_id, entity_type, alias, is_canonical, created_at, embedding)
			VALUES (?, ?, ?, ?, ?, ?), (?, ?, ?, ?, ?, ?), (?, ?, ?, ?, ?, ?)
			""";

		(string Id, string Type, string Alias, float[] Embedding)[] rows = [
			("e-acme",   "organization", "Acme Corp",      Embed(1f, 0f, 0f, 0f)),
			("e-zenith", "organization", "Zenith Widgets", Embed(0f, 1f, 0f, 0f)),
			("e-paris",  "location",     "Paris",          Embed(0f, 0f, 1f, 0f)),
		];

		dataSource.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText = sql;

			foreach (var row in rows) {
				command.Parameters.Add(new DuckDBParameter(row.Id));
				command.Parameters.Add(new DuckDBParameter(row.Type));
				command.Parameters.Add(new DuckDBParameter(row.Alias));
				command.Parameters.Add(new DuckDBParameter(true));
				command.Parameters.Add(new DuckDBParameter(0L));
				command.Parameters.Add(new DuckDBParameter(row.Embedding));
			}

			command.ExecuteNonQuery();
		});

		return dataSource;
	}

	#endregion
}
