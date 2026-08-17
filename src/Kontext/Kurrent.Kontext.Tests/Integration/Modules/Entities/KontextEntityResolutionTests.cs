// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
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
/// Behavioural tests for <see cref="EntityVerdictExecutor"/> against a REAL DuckDB + Lance engine:
/// the review queue's consumer, applying the verdict a human (or a later judge) reached. Most tests
/// seed the ledger directly — a doubt's origin is the projection suite's concern — but the two
/// replay tests drive the REAL projection so the verdict is challenged by the same batch that filed
/// the doubt re-resolving from scratch.
/// </summary>
[Category("Integration")]
[Category("Entities")]
[Timeout(30_000)]
public class KontextEntityResolutionTests {
	const int Dimension = KontextSchemaTask.Dimension;

	static readonly DateTimeOffset Base = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask confirming_a_doubt_folds_the_loser_into_the_survivor(CancellationToken cancellationToken) {
		// Arrange — the busier John, the thinner Jon, and two more doubts hanging off Jon.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-john", "John Smith", "PERSON", Base) { MentionCount = 3 },
			new EntitySeed("ent-jon", "Jon Smith", "PERSON", Base.AddHours(1)) { Aliases = ["jon smith", "jonny"], MentionCount = 1 },
			new EntitySeed("ent-x", "Johnny Smyth", "PERSON", Base) { MentionCount = 1 },
			new EntitySeed("ent-y", "J Smith", "PERSON", Base) { MentionCount = 1 });

		EntitySeeding.Insert(dataSources,
			new MentionSeed("ent-john", "m1", "John Smith", Base),
			new MentionSeed("ent-john", "m2", "John Smith", Base.AddMinutes(1)),
			new MentionSeed("ent-john", "m3", "J. Smith", Base.AddMinutes(2)),
			new MentionSeed("ent-jon", "m4", "Jon Smith", Base.AddHours(1)));

		EntitySeeding.Insert(dataSources,
			new LinkSeed("ent-jon", "ent-john", Base.AddHours(1)) { Confidence = 0.94, Method = "fuzzy" },
			new LinkSeed("ent-jon", "ent-x", Base.AddHours(2)) { Confidence = 0.88 },
			new LinkSeed("ent-y", "ent-jon", Base.AddHours(3)) { Confidence = 0.86 });

		using var connection  = dataSources.OpenLanceWriter();

		var store = new KontextEntityStore(connection);

		// Act
		var resolution = await new EntityVerdictExecutor(connection, Dimension)
			.ApplyAsync("ent-jon", "ent-john", EntityLinkVerdict.SameEntity, ct: cancellationToken);

		// Assert — the receipt.
		await Assert.That(resolution.Status).IsEqualTo("confirmed");
		await Assert.That(resolution.SurvivorEntityId).IsEqualTo("ent-john");
		await Assert.That(resolution.MergedEntityId).IsEqualTo("ent-jon");
		await Assert.That(resolution.MentionsRefiled).IsEqualTo(1);
		await Assert.That(resolution.SurvivorMentionCount).IsEqualTo(4);
		await Assert.That(resolution.LinksRepointed).IsEqualTo(2);
		await Assert.That(resolution.LinksDropped).IsEqualTo(0);

		// Assert — the loser is gone and its spellings live on the survivor.
		await Assert.That(await store.GetAsync("ent-jon")).IsNull();

		var john = await store.GetAsync("ent-john");

		await Assert.That(john).IsNotNull();
		await Assert.That(john!.Name).IsEqualTo("John Smith");
		await Assert.That(john.Aliases).Contains("john smith").And.Contains("jon smith").And.Contains("jonny");

		// Assert — mentions refiled, count RECOUNTED from the mentions table.
		await Assert.That(john.MentionCount).IsEqualTo(4);
		await Assert.That((await store.ListMentionsOfEntityAsync("ent-john")).Select(mention => mention.MemoryId).ToList())
			.IsEquivalentTo(["m1", "m2", "m3", "m4"]);
		await Assert.That(await store.ListMentionsOfEntityAsync("ent-jon")).IsEmpty();

		// Assert — the survivor now answers to the loser's name.
		await Assert.That((await store.FindExactAsync("jon smith", "PERSON"))!.EntityId).IsEqualTo("ent-john");

		// Assert — the doubt is retired and the loser's other doubts point at the survivor.
		await Assert.That((await store.ListLinksAsync("confirmed", 10)).Select(link => (link.SourceEntityId, link.TargetEntityId)).ToList())
			.IsEquivalentTo([("ent-jon", "ent-john")]);

		await Assert.That((await store.ListLinksAsync("pending", 10)).Select(link => (link.SourceEntityId, link.TargetEntityId)).ToList())
			.IsEquivalentTo([("ent-john", "ent-x"), ("ent-y", "ent-john")]);
	}

	[Test]
	public async ValueTask rejecting_a_doubt_settles_it_and_touches_neither_entity(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-emily", "Emily Chen", "PERSON", Base) { MentionCount = 10 },
			new EntitySeed("ent-emilia", "Emilia Chen", "PERSON", Base.AddHours(1)) { MentionCount = 2 });

		EntitySeeding.Insert(dataSources,
			new MentionSeed("ent-emily", "m1", "Emily Chen", Base),
			new MentionSeed("ent-emilia", "m2", "Emilia Chen", Base.AddHours(1)));

		EntitySeeding.Insert(dataSources, new LinkSeed("ent-emilia", "ent-emily", Base.AddHours(1)) { Confidence = 0.84 });

		using var connection  = dataSources.OpenLanceWriter();

		var store = new KontextEntityStore(connection);

		// Act
		var resolution = await new EntityVerdictExecutor(connection, Dimension)
			.ApplyAsync("ent-emilia", "ent-emily", EntityLinkVerdict.DifferentEntities, ct: cancellationToken);

		// Assert — a verdict, not a merge: nothing about either entity moved.
		await Assert.That(resolution.Status).IsEqualTo("rejected");
		await Assert.That(resolution.SurvivorEntityId).IsEmpty();
		await Assert.That(resolution.MergedEntityId).IsEmpty();

		await Assert.That(await store.CountAsync()).IsEqualTo(2);

		var emily  = await store.GetAsync("ent-emily");
		var emilia = await store.GetAsync("ent-emilia");

		await Assert.That(emily!.MentionCount).IsEqualTo(10);
		await Assert.That(emily.Aliases).IsEquivalentTo(["emily chen"]);
		await Assert.That(emilia!.MentionCount).IsEqualTo(2);
		await Assert.That(emilia.Aliases).IsEquivalentTo(["emilia chen"]);

		// Assert — off the queue for good.
		await Assert.That(await store.ListLinksAsync("pending", 10)).IsEmpty();

		var rejected = await Assert.That(await store.ListLinksAsync("rejected", 10)).HasSingleItem();

		await Assert.That(rejected!.SourceEntityId).IsEqualTo("ent-emilia");
		await Assert.That(rejected.Confidence).IsEqualTo(0.84);
	}

	[Test]
	public async ValueTask a_named_survivor_outranks_the_mention_count_rule(CancellationToken cancellationToken) {
		// Arrange — the busy entry is the typo: the human knows which spelling is real, and that is
		// the highest-trust signal there is.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-emilia", "Emilia Chen", "PERSON", Base) { MentionCount = 10 },
			new EntitySeed("ent-emily", "Emily Chen", "PERSON", Base.AddHours(1)) { MentionCount = 2 });

		EntitySeeding.Insert(dataSources,
			new MentionSeed("ent-emilia", "m1", "Emilia Chen", Base),
			new MentionSeed("ent-emily", "m2", "Emily Chen", Base.AddHours(1)));

		EntitySeeding.Insert(dataSources, new LinkSeed("ent-emily", "ent-emilia", Base.AddHours(1)) { Confidence = 0.84 });

		using var connection  = dataSources.OpenLanceWriter();

		var store = new KontextEntityStore(connection);

		// Act
		var resolution = await new EntityVerdictExecutor(connection, Dimension)
			.ApplyAsync("ent-emily", "ent-emilia", EntityLinkVerdict.SameEntity, survivorEntityId: "ent-emily", ct: cancellationToken);

		// Assert — the thinner entry survived because the reviewer said so.
		await Assert.That(resolution.SurvivorEntityId).IsEqualTo("ent-emily");
		await Assert.That(resolution.MergedEntityId).IsEqualTo("ent-emilia");

		await Assert.That(await store.GetAsync("ent-emilia")).IsNull();

		var emily = await store.GetAsync("ent-emily");

		await Assert.That(emily!.Name).IsEqualTo("Emily Chen");
		await Assert.That(emily.Aliases).Contains("emilia chen");
		await Assert.That(emily.MentionCount).IsEqualTo(2);
	}

	[Test]
	public async ValueTask the_default_survivor_is_the_busier_entry_and_ties_break_on_first_seen(CancellationToken cancellationToken) {
		// Arrange — equal mention counts, so only the earlier first_seen separates them.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-late", "Kurrent Labs", "ORGANIZATION", Base.AddDays(1)) { MentionCount = 2 },
			new EntitySeed("ent-early", "Kurrent Lab", "ORGANIZATION", Base) { MentionCount = 2 });

		EntitySeeding.Insert(dataSources,
			new MentionSeed("ent-early", "m1", "Kurrent Lab", Base),
			new MentionSeed("ent-late", "m2", "Kurrent Labs", Base.AddDays(1)));

		EntitySeeding.Insert(dataSources, new LinkSeed("ent-late", "ent-early", Base.AddDays(1)));

		using var connection  = dataSources.OpenLanceWriter();

		// Act
		var resolution = await new EntityVerdictExecutor(connection, Dimension)
			.ApplyAsync("ent-late", "ent-early", EntityLinkVerdict.SameEntity, ct: cancellationToken);

		// Assert
		await Assert.That(resolution.SurvivorEntityId).IsEqualTo("ent-early");
		await Assert.That(resolution.MergedEntityId).IsEqualTo("ent-late");
		await Assert.That(await new KontextEntityStore(connection).GetAsync("ent-late")).IsNull();
	}

	[Test]
	public async ValueTask repointing_drops_doubts_that_would_double_back_or_duplicate(CancellationToken cancellationToken) {
		// Arrange — the reverse of the decided pair (becomes a self-link) and a doubt the survivor
		// already carries against the same third entity (becomes a duplicate).
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-john", "John Smith", "PERSON", Base) { MentionCount = 3 },
			new EntitySeed("ent-jon", "Jon Smith", "PERSON", Base.AddHours(1)) { MentionCount = 1 },
			new EntitySeed("ent-z", "Jonathan Smith", "PERSON", Base) { MentionCount = 1 });

		EntitySeeding.Insert(dataSources,
			new LinkSeed("ent-jon", "ent-john", Base.AddHours(1)),
			new LinkSeed("ent-john", "ent-jon", Base.AddHours(2)),
			new LinkSeed("ent-jon", "ent-z", Base.AddHours(3)),
			new LinkSeed("ent-john", "ent-z", Base.AddHours(4)));

		using var connection  = dataSources.OpenLanceWriter();

		var store = new KontextEntityStore(connection);

		// Act
		var resolution = await new EntityVerdictExecutor(connection, Dimension)
			.ApplyAsync("ent-jon", "ent-john", EntityLinkVerdict.SameEntity, ct: cancellationToken);

		// Assert — both repointed rows collapsed, so neither was filed and both originals retired.
		await Assert.That(resolution.LinksRepointed).IsEqualTo(0);
		await Assert.That(resolution.LinksDropped).IsEqualTo(2);

		await Assert.That((await store.ListLinksAsync("pending", 10)).Select(link => (link.SourceEntityId, link.TargetEntityId)).ToList())
			.IsEquivalentTo([("ent-john", "ent-z")]);

		await Assert.That((await store.ListLinksAsync("confirmed", 10)).Select(link => (link.SourceEntityId, link.TargetEntityId)).ToList())
			.IsEquivalentTo([("ent-jon", "ent-john")]);
	}

	[Test]
	public async ValueTask re_applying_the_same_verdict_changes_nothing(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-john", "John Smith", "PERSON", Base) { MentionCount = 2 },
			new EntitySeed("ent-jon", "Jon Smith", "PERSON", Base.AddHours(1)) { MentionCount = 1 });

		EntitySeeding.Insert(dataSources,
			new MentionSeed("ent-john", "m1", "John Smith", Base),
			new MentionSeed("ent-john", "m2", "John Smith", Base.AddMinutes(1)),
			new MentionSeed("ent-jon", "m3", "Jon Smith", Base.AddHours(1)));

		EntitySeeding.Insert(dataSources, new LinkSeed("ent-jon", "ent-john", Base.AddHours(1)));

		using var connection  = dataSources.OpenLanceWriter();

		var store    = new KontextEntityStore(connection);
		var executor = new EntityVerdictExecutor(connection, Dimension);

		// Act — the same verdict twice, as a retrying caller or a re-reviewed row would.
		var first  = await executor.ApplyAsync("ent-jon", "ent-john", EntityLinkVerdict.SameEntity, ct: cancellationToken);
		var second = await executor.ApplyAsync("ent-jon", "ent-john", EntityLinkVerdict.SameEntity, ct: cancellationToken);

		// Assert — the second call recognizes a decided doubt and touches nothing.
		await Assert.That(first.WasAlreadyDecided).IsFalse();
		await Assert.That(second.WasAlreadyDecided).IsTrue();
		await Assert.That(second.Status).IsEqualTo("confirmed");

		var john = await store.GetAsync("ent-john");

		await Assert.That(john!.MentionCount).IsEqualTo(3);
		await Assert.That(john.Aliases).Contains("jon smith");
		await Assert.That(await store.CountAsync()).IsEqualTo(1);
		await Assert.That((await store.ListLinksAsync("confirmed", 10)).Count).IsEqualTo(1);

		// Assert — and a reviewer changing their mind after the fact cannot re-open it either.
		var reversal = await executor.ApplyAsync("ent-jon", "ent-john", EntityLinkVerdict.DifferentEntities, ct: cancellationToken);

		await Assert.That(reversal.WasAlreadyDecided).IsTrue();
		await Assert.That(reversal.Status).IsEqualTo("confirmed");
		await Assert.That(await store.CountAsync()).IsEqualTo(1);
	}

	[Test]
	public async ValueTask a_replay_of_the_batch_that_filed_the_doubt_cannot_undo_a_merge(CancellationToken cancellationToken) {
		// Arrange — the real write path files the doubt: "Jon Smith" against "John Smith" scores
		// inside the flag band, so two entities and one pending link land.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		using var connection  = dataSources.OpenLanceWriter();

		var store   = new KontextEntityStore(connection);
		var harness = NewHarness(connection);

		var batch = new[] {
			CreateRecord(NewRetained("m1", "PERSON=John Smith", Base), position: 100),
			CreateRecord(NewRetained("m2", "PERSON=Jon Smith", Base.AddMinutes(1)), position: 200),
		};

		await Project(harness, batch);

		var link = await Assert.That(await store.ListLinksAsync("pending", 10)).HasSingleItem();

		var resolution = await new EntityVerdictExecutor(connection, Dimension)
			.ApplyAsync(link!.SourceEntityId, link.TargetEntityId, EntityLinkVerdict.SameEntity, ct: cancellationToken);

		// Act — the crash-between-batch-and-checkpoint case, AFTER the review: the whole batch
		// re-projects and re-applies.
		await Project(harness, batch);

		// Assert — the merge held. The loser's name now resolves to the survivor, so the replay
		// files both memories under it instead of re-splitting, and the writer's insert-only link
		// arm leaves the verdict alone.
		await Assert.That(await store.CountAsync()).IsEqualTo(1);

		var survivor = await store.GetAsync(resolution.SurvivorEntityId);

		await Assert.That(survivor).IsNotNull();
		await Assert.That(survivor!.MentionCount).IsEqualTo(2);
		await Assert.That(survivor.Aliases).Contains("john smith").And.Contains("jon smith");

		await Assert.That(await store.GetAsync(resolution.MergedEntityId)).IsNull();
		await Assert.That(await store.ListLinksAsync("pending", 10)).IsEmpty();
		await Assert.That((await store.ListLinksAsync("confirmed", 10)).Count).IsEqualTo(1);
	}

	[Test]
	public async ValueTask a_replay_of_the_batch_that_filed_the_doubt_cannot_reopen_a_rejection(CancellationToken cancellationToken) {
		// Arrange — same filed doubt, ruled the other way.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		using var connection  = dataSources.OpenLanceWriter();

		var store   = new KontextEntityStore(connection);
		var harness = NewHarness(connection);

		var batch = new[] {
			CreateRecord(NewRetained("m1", "PERSON=John Smith", Base), position: 100),
			CreateRecord(NewRetained("m2", "PERSON=Jon Smith", Base.AddMinutes(1)), position: 200),
		};

		await Project(harness, batch);

		var link = await Assert.That(await store.ListLinksAsync("pending", 10)).HasSingleItem();

		await new EntityVerdictExecutor(connection, Dimension)
			.ApplyAsync(link!.SourceEntityId, link.TargetEntityId, EntityLinkVerdict.DifferentEntities, ct: cancellationToken);

		// Act
		await Project(harness, batch);

		// Assert — the writer's link MERGE inserts only WHEN NOT MATCHED, so the row keeps the
		// verdict instead of being reborn 'pending'; the doubt is never re-litigated.
		await Assert.That(await store.CountAsync()).IsEqualTo(2);
		await Assert.That(await store.ListLinksAsync("pending", 10)).IsEmpty();

		var rejected = await Assert.That(await store.ListLinksAsync("rejected", 10)).HasSingleItem();

		await Assert.That(rejected!.SourceEntityId).IsEqualTo(link.SourceEntityId);
		await Assert.That(rejected.TargetEntityId).IsEqualTo(link.TargetEntityId);
	}

	[Test]
	public async ValueTask a_verdict_on_an_unknown_or_degenerate_pair_is_refused(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-a", "Ada Lovelace", "PERSON", Base),
			new EntitySeed("ent-b", "Ada Byron", "PERSON", Base));

		EntitySeeding.Insert(dataSources, new LinkSeed("ent-b", "ent-a", Base));

		using var connection  = dataSources.OpenLanceWriter();

		var executor = new EntityVerdictExecutor(connection, Dimension);

		// Act + Assert — no such doubt, an endpoint that is not one, and a pair that names one entity.
		await Assert.That(async () => await executor.ApplyAsync("ent-a", "ent-b", EntityLinkVerdict.SameEntity, ct: cancellationToken))
			.Throws<InvalidOperationException>();

		await Assert.That(async () => await executor.ApplyAsync("ent-b", "ent-a", EntityLinkVerdict.SameEntity, "ent-z", cancellationToken))
			.Throws<ArgumentException>();

		await Assert.That(async () => await executor.ApplyAsync("ent-a", "ent-a", EntityLinkVerdict.SameEntity, ct: cancellationToken))
			.Throws<ArgumentException>();

		// Assert — a refused verdict leaves the queue exactly as it was.
		await Assert.That((await new KontextEntityStore(connection).ListLinksAsync("pending", 10)).Count).IsEqualTo(1);
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

	static async ValueTask Project((KontextEntityProjection Projection, KontextEntityWriter Writer) harness, SurgeRecord[] batch) =>
		harness.Writer.Apply(await harness.Projection.ProjectAsync(batch, CancellationToken.None));

	static (KontextEntityProjection Projection, KontextEntityWriter Writer) NewHarness(DuckDBAdvancedConnection connection) =>
		(new KontextEntityProjection(
				EntityExtractionPipeline.From([new MarkupExtractor()]),
				new OneHotEmbeddingGenerator(),
				new EmbeddingGenerationOptions { Dimensions = Dimension },
				new KontextEntityStore(connection)),
			new KontextEntityWriter(connection, Dimension));

	/// <summary>Deterministic extraction: content is <c>TYPE=Name; TYPE=Name</c> markup.</summary>
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
	/// One-hot embeddings: every distinct value gets its own axis, so semantic resolution stays
	/// silent and the string metrics decide — which is what puts "Jon Smith" in the flag band.
	/// </summary>
	sealed class OneHotEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>> {
		readonly Dictionary<string, int> _axes = [];

		public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
			IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default
		) {
			var results = new GeneratedEmbeddings<Embedding<float>>();

			foreach (var value in values)
				results.Add(new Embedding<float>(Embed(value)));

			return Task.FromResult(results);
		}

		float[] Embed(string value) {
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
