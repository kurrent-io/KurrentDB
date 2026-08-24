// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Kontext.Contracts.V3.Entities;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Entities;
using Kurrent.Kontext.Entities.Extraction;
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

	[Test]
	public async ValueTask resolution_pipeline_links_merges_and_creates_in_one_pass() {
		// Arrange — only the two exact misses may reach the embedding model.
		using var dir        = new TempDir();
		using var dataSource = await Seed(dir.Path);

		var embeddings = new MappedEmbeddings {
			["Globex"]           = Embed(1f, 0f, 0f, 0f),
			["Wayne Industries"] = Embed(0f, 0f, 0f, 1f),
		};

		var resolver = new KontextEntityResolver(dataSource, embeddings);

		// Act — one pass, three fates: "ACME Corp" matches an alias exactly; "Globex" misses but
		// its embedding sits ON Acme's vector, so it merges above the auto-merge threshold;
		// "Wayne Industries" resembles nothing and becomes a new entity.
		var resolved = await resolver.ResolveAsync([
			new ExtractedEntity("ACME Corp", "organization", 0.9),
			new ExtractedEntity("Globex", "organization", 0.9),
			new ExtractedEntity("Wayne Industries", "organization", 0.9),
		]);

		// Assert
		var exact   = resolved[EntityKey.For("organization", "ACME Corp")];
		var merged  = resolved[EntityKey.For("organization", "Globex")];
		var created = resolved[EntityKey.For("organization", "Wayne Industries")];

		await Assert.That(exact.EntityId).IsEqualTo("e-acme");
		await Assert.That(exact.Method).IsEqualTo(ResolutionMethod.Exact);

		await Assert.That(merged.EntityId).IsEqualTo("e-acme");
		await Assert.That(merged.Method).IsEqualTo(ResolutionMethod.Semantic);
		await Assert.That(merged.Confidence).IsEqualTo(1.0).Within(Tolerance);

		await Assert.That(created.EntityId).IsEqualTo(EntityId.For("organization", "Wayne Industries"));
		await Assert.That(created.Method).IsEqualTo(ResolutionMethod.Created);
		await Assert.That(created.Confidence).IsEqualTo(1.0).Within(Tolerance);
	}

	[Test]
	public async ValueTask the_model_settles_names_no_spelling_rule_can_reach() {
		// Arrange — "adoption interview" shares a word with the catalog's "adoption meeting" and
		// nothing else: no exact hit, no shared stem, and Jaro-Winkler is refused for multiword
		// forms. It reaches the catalog only if something can read both names.
		using var dir        = new TempDir();
		using var dataSource = await Seed(dir.Path);

		// Cosine 0.707 to the stored "adoption meeting" vector: near enough to come back as a
		// candidate, and short of the bar even after the two names' Jaro-Winkler (0.90, on the
		// shared "adoption ") corroborates it — (0.707 + 0.902) / 2 = 0.80 against 0.90.
		var embeddings = new MappedEmbeddings { ["adoption interview"] = Embed(0.707f, 0.707f, 0f, 0f) };
		var judge      = new ScriptedDisambiguator(("adoption interview", "adoption meeting"));

		var resolver = new KontextEntityResolver(dataSource, embeddings, options: null, judge);

		// Act
		var resolved = await resolver.ResolveAsync([new ExtractedEntity("adoption interview", "event", 0.9)]);

		// Assert — merged on the model's word, at the tier's own confidence, and the candidate it
		// was offered is one a cheaper tier surfaced and refused.
		var match = resolved[EntityKey.For("event", "adoption interview")];

		await Assert.That(match.EntityId).IsEqualTo("e-meeting");
		await Assert.That(match.Method).IsEqualTo(ResolutionMethod.Llm);
		await Assert.That(match.Confidence).IsEqualTo(0.95).Within(Tolerance);
		await Assert.That(judge.Offered).Contains("adoption meeting");
	}

	[Test]
	public async ValueTask an_abstaining_model_creates_rather_than_guesses() {
		// Arrange — the same name, and a model that declines every choice. Abstention must cost a
		// duplicate entity, never a merge: nothing in this system splits a wrong merge back apart.
		using var dir        = new TempDir();
		using var dataSource = await Seed(dir.Path);

		var embeddings = new MappedEmbeddings { ["adoption interview"] = Embed(0.707f, 0.707f, 0f, 0f) };

		var resolver = new KontextEntityResolver(
			dataSource, embeddings, options: null, new ScriptedDisambiguator());

		// Act
		var resolved = await resolver.ResolveAsync([new ExtractedEntity("adoption interview", "event", 0.9)]);

		// Assert
		var match = resolved[EntityKey.For("event", "adoption interview")];

		await Assert.That(match.EntityId).IsEqualTo(EntityId.For("event", "adoption interview"));
		await Assert.That(match.Method).IsEqualTo(ResolutionMethod.Created);
	}

	[Test]
	public async ValueTask a_unique_prefix_still_merges_when_no_model_is_configured() {
		// Arrange — "Mel" prefixes exactly one person in the catalog and nothing else claims it.
		// With no disambiguator the resolver falls back to merging on that alone, so switching the
		// model off costs recall on the hard names and never the nickname the tier used to handle.
		using var dir        = new TempDir();
		using var dataSource = await Seed(dir.Path);

		var embeddings = new MappedEmbeddings { ["Mel"] = Embed(0f, 0f, 0f, 1f) };
		var resolver   = new KontextEntityResolver(dataSource, embeddings);

		// Act
		var resolved = await resolver.ResolveAsync([new ExtractedEntity("Mel", "person", 0.9)]);

		// Assert
		await Assert.That(resolved[EntityKey.For("person", "Mel")].EntityId).IsEqualTo("e-melanie");
	}

	[Test]
	public async ValueTask resolution_pipeline_merges_on_spelling_where_spelling_is_evidence() {
		// Arrange — the names the lexical tier must claim and the ones it must refuse, in one pass.
		// The refusals fall through to the semantic tier, so they need an embedding that resembles
		// nothing: a refusal is only correct if it ends in a NEW entity, not merely in a miss.
		using var dir        = new TempDir();
		using var dataSource = await Seed(dir.Path);

		// The refused names embed onto an axis no same-type alias occupies, so nothing rescues them.
		var embeddings = new MappedEmbeddings {
			["acme"]               = Embed(0f, 0f, 1f, 0f),
			["adoption interview"] = Embed(0f, 0f, 1f, 0f),
		};

		var resolver = new KontextEntityResolver(dataSource, embeddings);

		// Act
		var resolved = await resolver.ResolveAsync([
			// Claimed: "acme corps" folds onto the stored "Acme Corp"; "mel" extends to the stored
			// "Melanie" and "melanies" folds back onto it — a prefix claim runs both ways.
			new ExtractedEntity("acme corps", "organization", 0.9),
			new ExtractedEntity("mel", "person", 0.9),
			new ExtractedEntity("melanies", "person", 0.9),
			// Refused: a bare label prefixing an org name is a shared label, not a nickname
			// ("Riverside" would claim Riverside Library, Riverside Clinic, Riverside Cafe, each
			// merge growing the blob the next one joins). And "adoption interview" shares a long prefix with the
			// stored "adoption meeting", so Jaro-Winkler calls them near-twins — but a shared
			// phrase head is shared context, not shared identity.
			new ExtractedEntity("acme", "organization", 0.9),
			new ExtractedEntity("adoption interview", "event", 0.9),
		]);

		// Assert — the three claims landed on the catalog's entities, by spelling alone.
		await Assert.That(resolved[EntityKey.For("organization", "acme corps")].EntityId).IsEqualTo("e-acme");
		await Assert.That(resolved[EntityKey.For("organization", "acme corps")].Confidence).IsEqualTo(0.97).Within(Tolerance);

		await Assert.That(resolved[EntityKey.For("person", "mel")].EntityId).IsEqualTo("e-melanie");
		await Assert.That(resolved[EntityKey.For("person", "mel")].Confidence).IsEqualTo(0.90).Within(Tolerance);
		await Assert.That(resolved[EntityKey.For("person", "melanies")].EntityId).IsEqualTo("e-melanie");

		// Assert — the two refusals became their own entities rather than joining a neighbour.
		await Assert.That(resolved[EntityKey.For("organization", "acme")].Method).IsEqualTo(ResolutionMethod.Created);
		await Assert.That(resolved[EntityKey.For("event", "adoption interview")].Method).IsEqualTo(ResolutionMethod.Created);
	}

	[Test]
	public async ValueTask created_entities_are_remembered_for_repeat_mentions() {
		// Arrange
		using var dir        = new TempDir();
		using var dataSource = await Seed(dir.Path);

		var embeddings = new MappedEmbeddings { ["Wayne Industries"] = Embed(0f, 0f, 0f, 1f) };
		var resolver   = new KontextEntityResolver(dataSource, embeddings);

		var key = EntityKey.For("organization", "Wayne Industries");

		// Act — the projector has not caught up (the catalog never sees the creation), yet the
		// repeat mention must link to the created entity instead of re-creating it.
		var first  = await resolver.ResolveAsync([new ExtractedEntity("Wayne Industries", "organization", 0.9)]);
		var second = await resolver.ResolveAsync([new ExtractedEntity("wayne INDUSTRIES", "organization", 0.9)]);

		// Assert — same id, linked exactly, and the second pass never embedded.
		await Assert.That(first[key].Method).IsEqualTo(ResolutionMethod.Created);
		await Assert.That(second[key].EntityId).IsEqualTo(first[key].EntityId);
		await Assert.That(second[key].Method).IsEqualTo(ResolutionMethod.Exact);
		await Assert.That(embeddings.Calls).IsEqualTo(1);
	}

	#region ->> Test Infrastructure <<-

	/// <summary>
	/// Embeds only the texts a test mapped, and counts calls — an unmapped text failing loudly
	/// pins WHICH spans the pipeline embeds, and the counter pins WHEN it embeds at all.
	/// </summary>
	sealed class MappedEmbeddings : IEmbeddingGenerator<string, Embedding<float>> {
		readonly Dictionary<string, float[]> _vectors = [];

		public int Calls { get; private set; }

		public float[] this[string text] { set => _vectors[text] = value; }

		public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
			IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default
		) {
			Calls++;

			var results = new GeneratedEmbeddings<Embedding<float>>();

			foreach (var value in values)
				results.Add(new Embedding<float>(_vectors[value]));

			return Task.FromResult(results);
		}

		public object? GetService(Type serviceType, object? serviceKey = null) => null;

		public void Dispose() { }
	}

	/// <summary>The primitives under test take embeddings as input, so the resolver never embeds here.</summary>
	/// <summary>
	/// A disambiguator that merges exactly the (name, candidate alias) pairs it was scripted with
	/// and abstains on everything else — the two answers the tier has to handle, without a model.
	/// </summary>
	sealed class ScriptedDisambiguator(params (string Text, string Alias)[] merges) : IEntityDisambiguator {
		readonly List<string> _offered = [];

		public IReadOnlyList<string> Offered => _offered;

		public ValueTask<IReadOnlyDictionary<EntityKey, string>> ResolveAsync(
			IReadOnlyCollection<Disambiguation> pending, CancellationToken ct = default
		) {
			var chosen = new Dictionary<EntityKey, string>();

			foreach (var item in pending) {
				_offered.AddRange(item.Candidates.Select(candidate => candidate.Alias));

				var match = item.Candidates.FirstOrDefault(candidate =>
					merges.Any(merge => merge.Text == item.Text && merge.Alias == candidate.Alias));

				if (match is not null)
					chosen[item.Key] = match.EntityId;
			}

			return ValueTask.FromResult<IReadOnlyDictionary<EntityKey, string>>(chosen);
		}
	}

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
		var padded = new float[KontextIndexConstants.VectorsDimension];
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
			INSERT INTO ldb.main.entities (entity_id, entity_type, alias, first_seen_at, embedding)
			VALUES (?, ?, ?, 0, ?), (?, ?, ?, 0, ?), (?, ?, ?, 0, ?), (?, ?, ?, 0, ?), (?, ?, ?, 0, ?)
			""";

		(string Id, string Type, string Alias, float[] Embedding)[] rows = [
			("e-acme",    "organization", "Acme Corp",        Embed(1f, 0f, 0f, 0f)),
			("e-zenith",  "organization", "Zenith Widgets",   Embed(0f, 1f, 0f, 0f)),
			("e-paris",   "location",     "Paris",            Embed(0f, 0f, 1f, 0f)),
			("e-melanie", "person",       "Melanie",          Embed(0f, 0f, 0f, 1f)),
			("e-meeting", "event",        "adoption meeting", Embed(0.5f, 0.5f, 0.5f, 0.5f)),
		];

		dataSource.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText = sql;

			foreach (var row in rows) {
				command.Parameters.Add(new DuckDBParameter(row.Id));
				command.Parameters.Add(new DuckDBParameter(row.Type));
				command.Parameters.Add(new DuckDBParameter(row.Alias));
				command.Parameters.Add(new DuckDBParameter(row.Embedding));
			}

			command.ExecuteNonQuery();
		});

		return dataSource;
	}

	#endregion
}
