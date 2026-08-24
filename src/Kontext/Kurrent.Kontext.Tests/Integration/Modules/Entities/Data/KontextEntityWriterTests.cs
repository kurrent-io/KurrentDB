// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Entities;
using Kurrent.Kontext.Entities.Data;
using Kurrent.Quack;
using Kurrent.Surge;
using Kurrent.Surge.Schema;
using Microsoft.Extensions.AI;
using EntityContracts = Kurrent.Kontext.Contracts.V3.Entities;

namespace Kurrent.Kontext.Tests.Modules.Entities.Data;

/// <summary>
/// Behavioural tests for <see cref="KontextEntityWriter"/> against a REAL DuckDB + Lance engine:
/// each test fabricates <see cref="SurgeRecord"/>s from <c>EntitiesMentioned</c> events and applies
/// them in BATCHES through <c>ProjectAsync</c> — the same unit of work the entity projector hands
/// over per <c>ReadBatches</c> window (no consumer, no checkpoint loop; those belong to Surge).
/// Catalog rows are asserted with direct SQL, and the write→read loop is closed through the real
/// <see cref="KontextEntityResolver"/>.
/// </summary>
[Category("Integration")]
[Timeout(30_000)]
public class KontextEntityWriterTests {
	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask created_and_linked_mentions_project_into_the_catalog(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		var writer = NewWriter(connection);

		// Act — one event: two spans naming two different entities, one of which resolution named
		// here and one it recognised. The writer is told nothing about which is which.
		await Project(writer, CreateRecord(NewMentioned(
			"m1", Base,
			Mention("Acme Corp", "e-acme", "organization", 1.0, EntityContracts.ResolutionMethod.Created),
			Mention("Zenith", "e-zenith", "organization", 0.87, EntityContracts.ResolutionMethod.Semantic)), position: 100));

		// Assert — one row per spelling seen, whichever tier found its entity. A mention naming an
		// entity the catalog has never held IS how the catalog comes to hold it.
		await Assert.That(CountRows(dataSources, "entities")).IsEqualTo(2L);

		var acme = ReadAlias(dataSources, "e-acme", "Acme Corp");

		await Assert.That(acme.EntityType).IsEqualTo("organization");
		await Assert.That(acme.FirstSeenAt).IsEqualTo(Base.ToUnixTimeMilliseconds());
		await Assert.That(acme.EmbeddingMatches).IsTrue();

		var zenith = ReadAlias(dataSources, "e-zenith", "Zenith");

		await Assert.That(zenith.EntityType).IsEqualTo("organization");
		await Assert.That(zenith.EmbeddingMatches).IsTrue();

		// Assert — both spans landed as mentions, pointing at their entity.
		await Assert.That(CountRows(dataSources, "entity_mentions")).IsEqualTo(2L);

		var created = ReadMention(dataSources, "m1", spanIndex: 0);

		await Assert.That(created.SpanText).IsEqualTo("Acme Corp");
		await Assert.That(created.EntityId).IsEqualTo("e-acme");
		await Assert.That(created.Confidence).IsEqualTo(1.0).Within(0.001);
		await Assert.That(created.ResolvedBy).IsEqualTo((int)EntityContracts.ResolutionMethod.Created);
		await Assert.That(created.LinkedAt).IsEqualTo(Base.ToUnixTimeMilliseconds());

		var linked = ReadMention(dataSources, "m1", spanIndex: 1);

		await Assert.That(linked.EntityId).IsEqualTo("e-zenith");
		await Assert.That(linked.Confidence).IsEqualTo(0.87).Within(0.001);
		await Assert.That(linked.ResolvedBy).IsEqualTo((int)EntityContracts.ResolutionMethod.Semantic);
	}

	[Test]
	public async ValueTask replaying_the_same_batch_leaves_the_catalog_unchanged(CancellationToken cancellationToken) {
		// Arrange — the same record applied in two successive batches: exactly what a crash between
		// an applied batch and its checkpoint produces on restart. Both MERGEs must fold the replay.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		var writer = NewWriter(connection);
		var record = CreateRecord(NewMentioned(
			"m1", Base,
			Mention("Acme Corp", "e-acme", "organization", 1.0, EntityContracts.ResolutionMethod.Created),
			Mention("Zenith", "e-zenith", "organization", 0.87, EntityContracts.ResolutionMethod.Semantic)), position: 100);

		// Act
		await Project(writer, record);
		await Project(writer, record);

		// Assert — no duplicate rows, values intact.
		await Assert.That(CountRows(dataSources, "entities")).IsEqualTo(2L);
		await Assert.That(CountRows(dataSources, "entity_mentions")).IsEqualTo(2L);
		await Assert.That(ReadAlias(dataSources, "e-acme", "Acme Corp").EmbeddingMatches).IsTrue();
		await Assert.That(ReadMention(dataSources, "m1", spanIndex: 0).EntityId).IsEqualTo("e-acme");
	}

	[Test]
	public async ValueTask a_replayed_mention_updates_in_place_when_its_resolution_changed(CancellationToken cancellationToken) {
		// Arrange — a later reconciliation pass re-resolves the same span against a grown catalog:
		// the (memory, span index) identity is stable, so the mention must update, never duplicate.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		var writer     = NewWriter(connection);
		var resolvedAt = Base.AddHours(1);

		await Project(writer, CreateRecord(NewMentioned(
			"m1", Base, Mention("Acme", "e-provisional", "organization", 0.6, EntityContracts.ResolutionMethod.FullText)), position: 100));

		// Act
		await Project(writer, CreateRecord(NewMentioned(
			"m1", resolvedAt, Mention("Acme", "e-acme", "organization", 0.99, EntityContracts.ResolutionMethod.Semantic)), position: 200));

		// Assert — still one row, carrying the new resolution.
		await Assert.That(CountRows(dataSources, "entity_mentions")).IsEqualTo(1L);

		var mention = ReadMention(dataSources, "m1", spanIndex: 0);

		await Assert.That(mention.EntityId).IsEqualTo("e-acme");
		await Assert.That(mention.Confidence).IsEqualTo(0.99).Within(0.001);
		await Assert.That(mention.ResolvedBy).IsEqualTo((int)EntityContracts.ResolutionMethod.Semantic);
		await Assert.That(mention.LinkedAt).IsEqualTo(resolvedAt.ToUnixTimeMilliseconds());
	}

	[Test]
	public async ValueTask a_created_entity_becomes_resolvable_for_later_mentions(CancellationToken cancellationToken) {
		// Arrange — the loop the feature exists for: a creation event lands in the catalog, and a
		// FRESH resolver (no in-memory memoization) resolves later mentions against it, exactly and
		// semantically.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		await Project(NewWriter(connection), CreateRecord(NewMentioned(
			"m1", Base,
			Mention("Acme Corp", "e-acme", "organization", 1.0, EntityContracts.ResolutionMethod.Created)), position: 100));

		var resolver = new KontextEntityResolver(dataSources, new FakeEmbeddings());

		// Act + Assert — the written spelling exact-resolves, case-insensitively.
		var exact = await resolver.ResolveExactAsync([EntityKey.For("organization", "ACME CORP")], cancellationToken);

		await Assert.That(exact[new EntityKey("organization", "acme corp")]).IsEqualTo("e-acme");

		// Act + Assert — a span whose embedding sits exactly on the written alias vector scores 1,
		// proving the writer embedded the alias with its model, not garbage.
		var semantic = await resolver.ResolveSemanticAsync(
			[new SemanticQuery(new EntityKey("organization", "globex"), FakeEmbeddings.Embed("Acme Corp"))], cancellationToken);

		await Assert.That(semantic[new EntityKey("organization", "globex")].EntityId).IsEqualTo("e-acme");
		await Assert.That(semantic[new EntityKey("organization", "globex")].Confidence).IsEqualTo(1.0).Within(0.001);
	}

	[Test]
	public async ValueTask a_new_spelling_is_learned_and_a_known_one_costs_nothing(CancellationToken cancellationToken) {
		// Arrange — the catalog knows "Melanie"; a later memory says "Mel" and resolution links it
		// lexically. The catalog must LEARN the new form so the next "Mel" exact-resolves and a
		// query naming "Mel" reaches the entity — and must recognise a spelling it already holds
		// without writing anything, which is the whole reason nothing has to flag which is which.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		using var connection = dataSources.OpenLanceWriter();

		var writer = NewWriter(connection);

		await Project(writer, CreateRecord(NewMentioned(
			"m1", Base,
			Mention("Melanie", "e-melanie", "person", 1.0, EntityContracts.ResolutionMethod.Created)), position: 100));

		// Act — a spelling the catalog does not hold, and one it holds under a different case.
		var written = await Project(writer, CreateRecord(NewMentioned(
			"m2", Base.AddHours(1),
			Mention("Mel", "e-melanie", "person", 0.90, EntityContracts.ResolutionMethod.FullText),
			Mention("MELANIE", "e-melanie", "person", 1.0, EntityContracts.ResolutionMethod.Exact)), position: 200));

		// Assert — one thing under two spellings. "MELANIE" is the spelling the catalog already
		// had, so it added no row and cost no embedding.
		await Assert.That(written).IsEqualTo(1);
		await Assert.That(CountRows(dataSources, "entities")).IsEqualTo(2L);

		var learned = ReadAlias(dataSources, "e-melanie", "Mel");

		await Assert.That(learned.FirstSeenAt).IsEqualTo(Base.AddHours(1).ToUnixTimeMilliseconds());
		await Assert.That(learned.EmbeddingMatches).IsTrue();

		var resolver = new KontextEntityResolver(dataSources, new FakeEmbeddings());
		var exact    = await resolver.ResolveExactAsync([EntityKey.For("person", "MEL")], cancellationToken);

		await Assert.That(exact[new EntityKey("person", "mel")]).IsEqualTo("e-melanie");
	}

	#region ->> Test Infrastructure <<-

	static EntityContracts.EntitiesMentioned NewMentioned(
		string memoryId, DateTimeOffset resolvedAt, params EntityContracts.EntityMention[] mentions
	) => new() {
		MemoryId   = memoryId,
		ResolvedAt = Timestamp.FromDateTimeOffset(resolvedAt),
		Mentions   = { mentions },
	};

	/// <summary>
	/// One mention, the only shape there is: the spelling as written, and the entity it names.
	/// Nothing distinguishes a creation from a link but the method it reports, and the writer does
	/// not read that.
	/// </summary>
	static EntityContracts.EntityMention Mention(
		string spanText, string entityId, string entityType, double confidence,
		EntityContracts.ResolutionMethod method
	) => new() {
		SpanText   = spanText,
		EntityId   = entityId,
		EntityType = entityType,
		Confidence = confidence,
		ResolvedBy = method,
	};

	// The same shape as the memory writer tests' CreateRecord: the writer switches on Value and
	// never reads Data, so raw proto bytes and a cosmetic SchemaInfo are enough.
	static SurgeRecord CreateRecord<T>(T message, ulong position) where T : IMessage<T> =>
		new() {
			Id         = Guid.NewGuid(),
			Position   = RecordPosition.ForLog(position),
			Timestamp  = Base.UtcDateTime,
			SchemaInfo = new SchemaInfo($"$kontext-{typeof(T).Name.ToLowerInvariant()}", SchemaDataFormat.Json),
			Data       = message.ToByteArray(),
			Value      = message,
			ValueType  = typeof(T),
			SequenceId = position,
			Headers    = new Headers()
		};

	/// <summary>Applies one batch through the writer, exactly like the projector's batch loop would.</summary>
	static async ValueTask<int> Project(KontextEntityWriter writer, params SurgeRecord[] batch) =>
		await writer.ProjectAsync(batch, CancellationToken.None);



	static KontextEntityWriter NewWriter(DuckDBAdvancedConnection connection) =>
		new(connection, new FakeEmbeddings(), new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension });

	static (string EntityType, long FirstSeenAt, bool EmbeddingMatches) ReadAlias(
		KontextDataSource dataSource, string entityId, string alias
	) =>
		dataSource.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText =
				$"""
				 SELECT entity_type, first_seen_at, embedding = CAST($expected AS FLOAT[{KontextIndexConstants.VectorsDimension}])
				 FROM ldb.main.entities
				 WHERE entity_id = $entity_id AND alias = $alias
				 """;
			command.Parameters.Add(new("expected", FakeEmbeddings.Embed(alias)));
			command.Parameters.Add(new("entity_id", entityId));
			command.Parameters.Add(new("alias", alias));

			using var reader = command.ExecuteReader();
			reader.Read();

			return (reader.GetString(0), reader.GetInt64(1), reader.GetBoolean(2));
		});

	static (string SpanText, string EntityId, double Confidence, int ResolvedBy, long LinkedAt) ReadMention(
		KontextDataSource dataSource, string memoryId, int spanIndex
	) =>
		dataSource.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText =
				"""
				SELECT span_text, entity_id, confidence, resolved_by, linked_at
				FROM ldb.main.entity_mentions
				WHERE memory_id = $memory_id AND span_index = $span_index
				""";
			command.Parameters.Add(new("memory_id", memoryId));
			command.Parameters.Add(new("span_index", spanIndex));

			using var reader = command.ExecuteReader();
			reader.Read();

			return (
				reader.GetString(0),
				reader.GetString(1),
				Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture),
				reader.GetInt32(3),
				reader.GetInt64(4));
		});

	static long CountRows(KontextDataSource dataSource, string table) =>
		dataSource.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText = $"SELECT count(*) FROM ldb.main.{table}";
			return (long)command.ExecuteScalar()!;
		});

	/// <summary>
	/// Deterministic schema-dimension embeddings: a unit vector on the axis picked by the text's
	/// length, so a test can recompute the exact vector the writer wrote and compare it in SQL.
	/// </summary>
	sealed class FakeEmbeddings : IEmbeddingGenerator<string, Embedding<float>> {
		public static float[] Embed(string text) {
			var vector = new float[KontextIndexConstants.VectorsDimension];
			vector[text.Length % 4] = 1f;
			return vector;
		}

		public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
			IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default
		) {
			var results = new GeneratedEmbeddings<Embedding<float>>();

			foreach (var value in values)
				results.Add(new Embedding<float>(Embed(value)));

			return Task.FromResult(results);
		}

		public object? GetService(System.Type serviceType, object? serviceKey = null) => null;

		public void Dispose() { }
	}

	#endregion // Test Infrastructure
}
