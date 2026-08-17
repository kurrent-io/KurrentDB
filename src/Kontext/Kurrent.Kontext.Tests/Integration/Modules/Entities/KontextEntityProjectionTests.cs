// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Modules.Entities;
using Kurrent.Kontext.Modules.Entities.Data;
using Kurrent.Kontext.Modules.Entities.Extraction;
using Kurrent.Kontext.Modules.Entities.Resolution;
using Kurrent.Quack;
using Kurrent.Surge;
using Kurrent.Surge.Schema;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Tests.Integration.Modules.Entities;

/// <summary>
/// Behavioural tests for <see cref="KontextEntityProjection"/> + <see cref="KontextEntityWriter"/>
/// against a REAL DuckDB + Lance engine — the projection computes the delta, the writer applies it:
/// each test fabricates <see cref="SurgeRecord"/>s carrying <see cref="Contracts.MemoriesRetained"/>
/// events whose content is a tiny <c>TYPE=Name;</c> markup, extracted by a deterministic markup
/// extractor — extraction QUALITY belongs to the extractor tests; this suite proves the
/// resolve → dedup → write machinery. Embeddings are one-hot per distinct name (orthogonal), so
/// semantic matches only happen where a test explicitly plants near vectors.
/// </summary>
[Category("Integration")]
[Category("Entities")]
[Timeout(30_000)]
public class KontextEntityProjectionTests {
	const int Dimension = KontextSchemaTask.Dimension;

	static readonly DateTimeOffset Base = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask extracts_new_entities_and_writes_mentions_with_provenance(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		using var connection  = dataSources.OpenLanceWriter();

		var store  = new KontextEntityStore(connection);
		var harness = NewHarness(connection);

		// Act — two memories in one batch, sharing one entity.
		await Project(harness,
			CreateRecord(NewRetained("m1", "PERSON=Ada Lovelace; ORGANIZATION=Kurrent", Base), position: 100),
			CreateRecord(NewRetained("m2", "PERSON=Ada Lovelace; LOCATION=London", Base.AddMinutes(1)), position: 200));

		// Assert — three entities: the shared person folded into one.
		await Assert.That(await store.CountAsync()).IsEqualTo(3);

		var ada = await store.FindExactAsync("ada lovelace", "PERSON");

		await Assert.That(ada).IsNotNull();
		await Assert.That(ada!.Name).IsEqualTo("Ada Lovelace");
		await Assert.That(ada.MentionCount).IsEqualTo(2);
		await Assert.That(ada.Aliases).Contains("ada lovelace");
		await Assert.That(ada.FirstSeen).IsEqualTo(Base.ToUnixTimeMilliseconds());
		await Assert.That(ada.LastSeen).IsEqualTo(Base.AddMinutes(1).ToUnixTimeMilliseconds());
		await Assert.That(ada.LogPosition).IsEqualTo(200L);

		// Assert — provenance walks both directions.
		var ofMemory = await store.ListMentionsOfMemoryAsync("m1");
		await Assert.That(ofMemory.Select(mention => mention.Surface).ToList()).IsEquivalentTo(["Ada Lovelace", "Kurrent"]);
		await Assert.That(ofMemory[0].Extractor).IsEqualTo("markup");

		var ofEntity = await store.ListMentionsOfEntityAsync(ada.EntityId);
		await Assert.That(ofEntity.Select(mention => mention.MemoryId).ToList()).IsEquivalentTo(["m1", "m2"]);
	}

	[Test]
	public async ValueTask exact_match_across_batches_merges_instead_of_duplicating(CancellationToken cancellationToken) {
		// Arrange — batch one creates the entity.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		using var connection  = dataSources.OpenLanceWriter();

		var store  = new KontextEntityStore(connection);
		var harness = NewHarness(connection);

		await Project(harness, CreateRecord(NewRetained("m1", "PERSON=Ada Lovelace", Base), position: 100));

		// Act — a later batch mentions the same person under wilder casing and spacing.
		await Project(harness, CreateRecord(NewRetained("m2", "PERSON=ADA   LOVELACE", Base.AddHours(1)), position: 200));

		// Assert — still one entity, both mentions counted, recency advanced.
		await Assert.That(await store.CountAsync()).IsEqualTo(1);

		var ada = await store.FindExactAsync("ada lovelace", "PERSON");
		await Assert.That(ada!.MentionCount).IsEqualTo(2);
		await Assert.That(ada.LastSeen).IsEqualTo(Base.AddHours(1).ToUnixTimeMilliseconds());
		await Assert.That(ada.Name).IsEqualTo("Ada Lovelace");
	}

	[Test]
	public async ValueTask near_identical_names_auto_merge_and_the_variant_becomes_an_alias(CancellationToken cancellationToken) {
		// Arrange — "Kurrent Databases" vs "Kurrent Database": token-sort similarity 1 - 1/33,
		// above the 0.95 auto-merge line.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		using var connection  = dataSources.OpenLanceWriter();

		var store  = new KontextEntityStore(connection);
		var harness = NewHarness(connection);

		// Act — same batch, two keys, one entity.
		await Project(harness,
			CreateRecord(NewRetained("m1", "ORGANIZATION=Kurrent Database", Base), position: 100),
			CreateRecord(NewRetained("m2", "ORGANIZATION=Kurrent Databases", Base.AddMinutes(1)), position: 200));

		// Assert
		await Assert.That(await store.CountAsync()).IsEqualTo(1);

		var merged = await store.FindExactAsync("kurrent database", "ORGANIZATION");
		await Assert.That(merged!.Aliases).Contains("kurrent databases");
		await Assert.That(merged.MentionCount).IsEqualTo(2);

		// And no review link — an auto-merge is not a suspicion.
		await Assert.That(await store.ListLinksAsync("pending", 10)).IsEmpty();
	}

	[Test]
	public async ValueTask suspiciously_close_names_create_separately_and_flag_for_review(CancellationToken cancellationToken) {
		// Arrange — "Jon Smith" vs "John Smith": similarity 1 - 1/19 ≈ 0.947, inside the
		// flag band (0.85 ≤ score < 0.95).
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		using var connection  = dataSources.OpenLanceWriter();

		var store  = new KontextEntityStore(connection);
		var harness = NewHarness(connection);

		await Project(harness, CreateRecord(NewRetained("m1", "PERSON=John Smith", Base), position: 100));

		// Act
		await Project(harness, CreateRecord(NewRetained("m2", "PERSON=Jon Smith", Base.AddHours(1)), position: 200));

		// Assert — two entities, one pending link pointing the new at the suspect.
		await Assert.That(await store.CountAsync()).IsEqualTo(2);

		var john = await store.FindExactAsync("john smith", "PERSON");
		var jon  = await store.FindExactAsync("jon smith", "PERSON");

		var pending = await store.ListLinksAsync("pending", 10);
		var link    = await Assert.That(pending).HasSingleItem();

		await Assert.That(link!.SourceEntityId).IsEqualTo(jon!.EntityId);
		await Assert.That(link.TargetEntityId).IsEqualTo(john!.EntityId);
		await Assert.That(link.Method).IsEqualTo("fuzzy");
		await Assert.That(link.Confidence).IsGreaterThanOrEqualTo(0.85).And.IsLessThan(0.95);
	}

	[Test]
	public async ValueTask semantic_neighbors_merge_when_strings_cannot_bridge_them(CancellationToken cancellationToken) {
		// Arrange — an embedder that places the abbreviation and the full name on near vectors;
		// every string metric scores them far apart.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		using var connection  = dataSources.OpenLanceWriter();

		var embedder = new FakeEmbeddingGenerator();
		embedder.Plant("IBM", EntitySeeding.Embedding(1f));
		embedder.Plant("International Business Machines", EntitySeeding.Embedding(0.999f, 0.04f));

		var store  = new KontextEntityStore(connection);
		var harness = NewHarness(connection, embedder);

		await Project(harness, CreateRecord(NewRetained("m1", "ORGANIZATION=IBM", Base), position: 100));

		// Act
		await Project(harness, CreateRecord(NewRetained("m2", "ORGANIZATION=International Business Machines", Base.AddHours(1)), position: 200));

		// Assert — one entity, the long form folded in as an alias.
		await Assert.That(await store.CountAsync()).IsEqualTo(1);

		var ibm = await store.FindExactAsync("ibm", "ORGANIZATION");
		await Assert.That(ibm!.Aliases).Contains("international business machines");
		await Assert.That(ibm.MentionCount).IsEqualTo(2);
	}

	[Test]
	public async ValueTask the_type_wall_keeps_same_names_apart(CancellationToken cancellationToken) {
		// Arrange + Act — the same word as a person and as a place, one batch.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		using var connection  = dataSources.OpenLanceWriter();

		var store  = new KontextEntityStore(connection);
		var harness = NewHarness(connection);

		await Project(harness, CreateRecord(NewRetained("m1", "PERSON=Jordan; LOCATION=Jordan", Base), position: 100));

		// Assert — two entities, never merged, never flagged.
		await Assert.That(await store.CountAsync()).IsEqualTo(2);
		await Assert.That(await store.ListLinksAsync("pending", 10)).IsEmpty();
	}

	[Test]
	public async ValueTask replay_of_a_batch_leaves_the_read_model_unchanged(CancellationToken cancellationToken) {
		// Arrange — the crash-between-batch-and-checkpoint case: the same batch applies twice.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		using var connection  = dataSources.OpenLanceWriter();

		var store  = new KontextEntityStore(connection);
		var harness = NewHarness(connection);

		var batch = new[] {
			CreateRecord(NewRetained("m1", "PERSON=John Smith; ORGANIZATION=Kurrent", Base), position: 100),
			CreateRecord(NewRetained("m2", "PERSON=Jon Smith", Base.AddMinutes(1)), position: 200),
		};

		// Act
		harness.Writer.Apply(await harness.Projection.ProjectAsync(batch, cancellationToken));
		harness.Writer.Apply(await harness.Projection.ProjectAsync(batch, cancellationToken));

		// Assert — deterministic ids and MERGE writes absorb the replay completely.
		await Assert.That(await store.CountAsync()).IsEqualTo(3);

		var john = await store.FindExactAsync("john smith", "PERSON");
		await Assert.That(john!.MentionCount).IsEqualTo(1);

		await Assert.That((await store.ListMentionsOfMemoryAsync("m1")).Count).IsEqualTo(2);
		await Assert.That((await store.ListLinksAsync("pending", 10)).Count).IsEqualTo(1);
	}

	[Test]
	public async ValueTask non_retain_events_and_entity_free_content_are_ignored(CancellationToken cancellationToken) {
		// Arrange + Act — a retraction record and a memory whose content extracts nothing.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		using var connection  = dataSources.OpenLanceWriter();

		var store  = new KontextEntityStore(connection);
		var harness = NewHarness(connection);

		var retracted = new Contracts.MemoryRetracted {
			MemoryId           = "m9",
			Reason             = "irrelevant to entities",
			RetractedMemoryIds = { "m9" },
			RetractedAt        = Timestamp.FromDateTimeOffset(Base),
		};

		await Project(harness,
			CreateRecord(retracted, position: 100),
			CreateRecord(NewRetained("m1", "no markup here at all", Base), position: 200));

		// Assert
		await Assert.That(await store.CountAsync()).IsEqualTo(0);
	}

	#region ->> Test Infrastructure <<-

	/// <summary>A single-memory MemoriesRetained event whose content is markup for the fake extractor.</summary>
	static Contracts.MemoriesRetained NewRetained(string memoryId, string content, DateTimeOffset retainedAt) => new() {
		Memories = {
			new Contracts.MemoriesRetained.Types.RetainedMemory {
				MemoryId = memoryId,
				Memory = new Contracts.Memory {
					MemoryType = Contracts.MemoryType.Fact,
					Content    = content,
					Importance = Contracts.MemoryImportance.Normal,
				},
			}
		},
		RetainedAt = Timestamp.FromDateTimeOffset(retainedAt),
	};

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

	static async ValueTask Project((KontextEntityProjection Projection, KontextEntityWriter Writer) harness, params SurgeRecord[] batch) =>
		harness.Writer.Apply(await harness.Projection.ProjectAsync(batch, CancellationToken.None));

	static (KontextEntityProjection Projection, KontextEntityWriter Writer) NewHarness(
		DuckDBAdvancedConnection connection, FakeEmbeddingGenerator? embedder = null
	) =>
		(new KontextEntityProjection(
				EntityExtractionPipeline.From([new MarkupExtractor()]),
				embedder ?? new FakeEmbeddingGenerator(),
				new EmbeddingGenerationOptions { Dimensions = Dimension },
				new KontextEntityStore(connection)),
			new KontextEntityWriter(connection, Dimension));

	/// <summary>
	/// Deterministic extraction: content is <c>TYPE=Name; TYPE=Name</c> markup. Extraction
	/// quality is the real extractors' concern; this suite tests everything after extraction.
	/// </summary>
	sealed class MarkupExtractor : IEntityExtractor {
		public string Name => "markup";

		public ValueTask<ExtractionResult> ExtractAsync(string text, CancellationToken ct = default) {
			var entities = new List<ExtractedEntity>();

			foreach (var part in text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
				var pieces = part.Split('=', 2);

				if (pieces.Length != 2)
					continue;

				entities.Add(new() {
					Name       = pieces[1].Trim(),
					Type       = pieces[0].Trim(),
					Confidence = 0.9,
					Extractor  = Name,
				});
			}

			return ValueTask.FromResult(new ExtractionResult { Entities = entities });
		}
	}

	/// <summary>
	/// One-hot embeddings: every distinct value gets its own axis (orthogonal to all others),
	/// so semantic resolution stays silent unless a test PLANTS near vectors deliberately.
	/// </summary>
	sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>> {
		readonly Dictionary<string, float[]> _planted = [];
		readonly Dictionary<string, int>     _axes    = [];

		public void Plant(string value, float[] vector) => _planted[value] = vector;

		public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
			IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default
		) {
			var results = new GeneratedEmbeddings<Embedding<float>>();

			foreach (var value in values)
				results.Add(new Embedding<float>(Embed(value)));

			return Task.FromResult(results);
		}

		float[] Embed(string value) {
			if (_planted.TryGetValue(value, out var planted))
				return planted;

			if (!_axes.TryGetValue(value, out var axis)) {
				axis = _axes.Count;

				if (axis >= Dimension)
					throw new InvalidOperationException($"The one-hot fake ran out of axes — this suite assumes fewer than {Dimension} distinct names per test.");

				_axes[value] = axis;
			}

			var vector = new float[Dimension];
			vector[axis] = 1f;
			return vector;
		}

		public object? GetService(System.Type serviceType, object? serviceKey = null) => null;

		public void Dispose() { }
	}

	#endregion // Test Infrastructure
}
