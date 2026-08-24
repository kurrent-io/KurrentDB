// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Google.Protobuf;
using TUnit.Assertions.Enums;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Memory;
using Kurrent.Kontext.Memory.Data;
using Kurrent.Kontext.Retrieval;
using Kurrent.Kontext.Configuration;
using Kurrent.Kontext.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;
using MemoryContracts = Kurrent.Kontext.Contracts.V3.Memory;

namespace Kurrent.Kontext.Tests;

/// <summary>
/// Behavioural tests for <see cref="KontextMemory"/> against a REAL DuckDB + Lance engine, over the
/// projector-owned <see cref="KontextMemoryDataStore"/> read model. The projector is not in the loop
/// here, so each test seeds the memories table directly with SQL — exactly how the projector writes
/// it — and exercises the surface the service exposes:
/// - retain decides each memory against the store, then appends one MemoriesRetained event for
///   whatever it wrote; reflect still throws
/// - recall is keyword-only BM25 search, lean by default, and never surfaces hidden memories
/// - reclaim is an exact-id passthrough that skips ids it does not hold
/// - recollect lists by type/tag with a sort
///
/// Embeddings are seeded as literal 4-dim vectors so the table is well-formed; recall here is
/// keyword-only, so the vectors never decide a result.
/// </summary>
[Category("Integration")]
public class KontextMemoryTests {
	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask retain_mints_ids_and_appends_one_event_for_the_whole_batch() {
		// Arrange — capture what reaches the log; the projector, not this service, applies it.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		// The dataset must exist even with nothing in it: retain searches for a duplicate before it
		// appends, so an unseeded store would fail the search rather than return none.
		await MemorySeeding.CreateSchema(dataSources);

		var appended = new List<MemoryContracts.MemoriesRetained>();
		var clock    = new FakeTimeProvider(Base);
		var store    = new KontextMemoryDataStore(dataSources);
		var memory   = NewMemory(store, Capture(appended), clock);

		var request = new MemoryContracts.RetainRequest();
		request.Memories.Add(new MemoryContracts.Memory { MemoryType = MemoryContracts.MemoryType.Fact, Content = "the suite runs via test-runner.cs" });
		request.Memories.Add(new MemoryContracts.Memory { MemoryType = MemoryContracts.MemoryType.Preference, Content = "prefers K&R braces" });

		var expectedCount = 2;

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — one event carries the batch, so no reader can observe half of it.
		await Assert.That(appended.Count).IsEqualTo(1);
		await Assert.That(appended[0].Memories.Count).IsEqualTo(expectedCount);
		await Assert.That(appended[0].RetainedAt.ToDateTimeOffset()).IsEqualTo(Base);

		// The server mints every id, and results[i] is the memory sent at memories[i].
		await Assert.That(response.Results.Count).IsEqualTo(expectedCount);
		await Assert.That(response.Results[0].MemoryId).IsEqualTo(appended[0].Memories[0].MemoryId);
		await Assert.That(response.Results[1].MemoryId).IsEqualTo(appended[0].Memories[1].MemoryId);
		await Assert.That(appended[0].Memories[0].Memory.Content).IsEqualTo("the suite runs via test-runner.cs");
		await Assert.That(appended[0].Memories[1].Memory.Content).IsEqualTo("prefers K&R braces");

		// Ids are fresh GUIDs, never the caller's and never repeated.
		await Assert.That(Guid.TryParse(response.Results[0].MemoryId, out _)).IsTrue();
		await Assert.That(response.Results[0].MemoryId).IsNotEqualTo(response.Results[1].MemoryId);
	}

	[Test]
	public async ValueTask retain_noops_when_identical_content_is_already_stored_under_the_same_tags() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		const string content = "the test runner lives at scripts/testing/test-runner.cs";

		var repo  = new MemoryContracts.Tag { Scope = "repo", Value = "kurrentdb" };
		var store = await Seed(dataSources,
			new Row("existing", MemoryContracts.MemoryType.Fact, content, MemoryContracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)) {
				Tags = [KontextMemoryDataStore.EncodeTag(repo)],
			});

		var appended = new List<MemoryContracts.MemoriesRetained>();
		var memory   = NewMemory(store, Capture(appended), TimeProvider.System);
		var request  = new MemoryContracts.RetainRequest();

		var incoming = new MemoryContracts.Memory { MemoryType = MemoryContracts.MemoryType.Fact, Content = content };
		incoming.Tags.Add(repo);
		request.Memories.Add(incoming);

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — the same claim under tags the store already covers adds nothing, so nothing is
		// written and the caller is handed the memory that already says it.
		await Assert.That(response.Results[0].Outcome).IsEqualTo(MemoryContracts.RetainOutcome.Noop);
		await Assert.That(response.Results[0].MemoryId).IsEqualTo("existing");
		await Assert.That(appended).IsEmpty();
	}

	[Test]
	public async ValueTask retain_merges_identical_content_that_arrives_with_a_new_tag() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		const string content = "the test runner lives at scripts/testing/test-runner.cs";

		var repo    = new MemoryContracts.Tag { Scope = "repo", Value = "kurrentdb" };
		var session = new MemoryContracts.Tag { Scope = "session", Value = "9fb733bd" };

		var store = await Seed(dataSources,
			new Row("existing", MemoryContracts.MemoryType.Fact, content, MemoryContracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)) {
				Tags     = [KontextMemoryDataStore.EncodeTag(repo)],
				Evidence = SeedEvidenceBlobs(),
			});

		var appended = new List<MemoryContracts.MemoriesRetained>();
		var memory   = NewMemory(store, Capture(appended), TimeProvider.System);
		var request  = new MemoryContracts.RetainRequest();

		var incoming = new MemoryContracts.Memory { MemoryType = MemoryContracts.MemoryType.Fact, Content = content };
		incoming.Tags.Add(session);
		request.Memories.Add(incoming);

		var expectedTags = new[] { session, repo };

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — a new tag widens the claim's reach, so the successor carries both tag sets and
		// both citation lists rather than leaving a second copy behind.
		await Assert.That(response.Results[0].Outcome).IsEqualTo(MemoryContracts.RetainOutcome.Merged);
		await Assert.That(response.Results[0].SupersededMemoryIds).IsEquivalentTo(["existing"]);

		var written = appended[0].Memories[0].Memory;

		await Assert.That(written.Tags).IsEquivalentTo(expectedTags, CollectionOrdering.Any);
		await Assert.That(written.Evidence).IsEquivalentTo([SeedEvidence()], CollectionOrdering.Any);
	}

	[Test]
	public async ValueTask retain_merges_the_nearest_memory_when_it_sits_inside_the_merge_band() {
		// Arrange — the stored vector and the query vector are identical, so the distance is 0 and
		// the pair sits well inside MergeCeiling.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var store = await Seed(dataSources,
			new Row("existing", MemoryContracts.MemoryType.Fact, "the projector checkpoints after the batch lands", MemoryContracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)) {
				Evidence = SeedEvidenceBlobs(),
			});

		var appended = new List<MemoryContracts.MemoriesRetained>();
		var memory   = NewMemory(store, Capture(appended), TimeProvider.System, new FixedEmbeddings(MemorySeeding.Vector(1f)));
		var request  = new MemoryContracts.RetainRequest();

		request.Memories.Add(new MemoryContracts.Memory {
			MemoryType = MemoryContracts.MemoryType.Fact,
			Content    = "the projector checkpoints once the batch has landed",
		});

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — support accumulates along the chain: the successor inherits what it replaced.
		await Assert.That(response.Results[0].Outcome).IsEqualTo(MemoryContracts.RetainOutcome.Merged);
		await Assert.That(response.Results[0].SupersededMemoryIds).IsEquivalentTo(["existing"]);
		await Assert.That(appended[0].Memories[0].Memory.Evidence).IsEquivalentTo([SeedEvidence()], CollectionOrdering.Any);
	}

	[Test]
	public async ValueTask retain_defers_and_writes_nothing_when_the_nearest_memory_is_ambiguous() {
		// Arrange — a query vector at cosine 0.25 to the stored one puts the pair at squared L2
		// 2 - 2(0.25) = 1.5, between MergeCeiling and AppendFloor.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var store = await Seed(dataSources,
			new Row("existing", MemoryContracts.MemoryType.Fact, "a colleague reported the outage during standup", MemoryContracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)));

		var appended = new List<MemoryContracts.MemoriesRetained>();
		var memory   = NewMemory(store, Capture(appended), TimeProvider.System, new FixedEmbeddings(Ambiguous));
		var request  = new MemoryContracts.RetainRequest();

		request.Memories.Add(new MemoryContracts.Memory {
			MemoryType = MemoryContracts.MemoryType.Fact,
			Content    = "someone mentioned the downtime at the morning meeting",
		});

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — too close to call means NOTHING was stored. The caller reads the candidates and
		// answers with `decided`; a server that guessed here would either duplicate or destroy.
		await Assert.That(response.Results[0].Outcome).IsEqualTo(MemoryContracts.RetainOutcome.Deferred);
		await Assert.That(response.Results[0].MemoryId).IsEmpty();
		await Assert.That(appended).IsEmpty();

		var candidates = response.Results[0].Candidates;

		await Assert.That(candidates).IsNotEmpty();
		await Assert.That(candidates[0].Memory.MemoryId).IsEqualTo("existing");
		await Assert.That(candidates[0].Distance).IsEqualTo(1.5).Within(1e-4);
	}

	[Test]
	public async ValueTask decided_creates_the_memory_the_server_would_otherwise_defer() {
		// Arrange — the same ambiguous distance as the deferral above.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var store = await Seed(dataSources,
			new Row("existing", MemoryContracts.MemoryType.Fact, "a colleague reported the outage during standup", MemoryContracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)));

		var appended = new List<MemoryContracts.MemoriesRetained>();
		var memory   = NewMemory(store, Capture(appended), TimeProvider.System, new FixedEmbeddings(Ambiguous));
		var request  = new MemoryContracts.RetainRequest { Decided = true };

		request.Memories.Add(new MemoryContracts.Memory {
			MemoryType = MemoryContracts.MemoryType.Fact,
			Content    = "someone mentioned the downtime at the morning meeting",
		});

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — without this the deferral has no answer for "I looked, and it is genuinely new",
		// and an ambiguous memory could never be written at all.
		await Assert.That(response.Results[0].Outcome).IsEqualTo(MemoryContracts.RetainOutcome.Created);
		await Assert.That(response.Results[0].SupersededMemoryIds).IsEmpty();
		await Assert.That(appended[0].Memories.Count).IsEqualTo(1);
	}

	[Test]
	public async ValueTask retain_creates_when_the_nearest_memory_is_beyond_the_append_floor() {
		// Arrange — an orthogonal query vector puts the pair at squared L2 2.0, above AppendFloor.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var store = await Seed(dataSources,
			new Row("existing", MemoryContracts.MemoryType.Fact, "penguins waddle across antarctic ice", MemoryContracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)));

		var appended = new List<MemoryContracts.MemoriesRetained>();
		var memory   = NewMemory(store, Capture(appended), TimeProvider.System, new FixedEmbeddings(MemorySeeding.Vector(0f, 1f)));
		var request  = new MemoryContracts.RetainRequest();

		request.Memories.Add(new MemoryContracts.Memory {
			MemoryType = MemoryContracts.MemoryType.Fact,
			Content    = "the certificate rotation job runs every ninety days",
		});

		// Act
		var response = await memory.RetainAsync(request);

		// Assert
		await Assert.That(response.Results[0].Outcome).IsEqualTo(MemoryContracts.RetainOutcome.Created);
		await Assert.That(response.Results[0].SupersededMemoryIds).IsEmpty();
		await Assert.That(appended[0].Memories[0].MemoryId).IsEqualTo(response.Results[0].MemoryId);
	}

	[Test]
	public async ValueTask reflect_throws_not_implemented() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = new KontextMemoryDataStore(dataSources);
		var       memory      = NewMemory(store, NoOp, TimeProvider.System);

		// Act + Assert
		await Assert.That(async () => await memory.ReflectAsync(new())).Throws<NotImplementedException>();
	}

	[Test]
	public async ValueTask recall_finds_seeded_memories_by_keywords() {
		// Arrange — three memories with fully distinct vocabularies; only a1 carries "aardvark".
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources,
			new Row("a1", MemoryContracts.MemoryType.Fact, "aardvark burrows deep underground", MemoryContracts.MemoryImportance.High, Base.AddHours(1), MemorySeeding.Vector(1f)),
			new Row("a2", MemoryContracts.MemoryType.Fact, "penguins waddle across antarctic ice", MemoryContracts.MemoryImportance.Normal, Base.AddHours(2), MemorySeeding.Vector(0f, 1f)),
			new Row("a3", MemoryContracts.MemoryType.Fact, "giraffes browse the tallest acacia leaves", MemoryContracts.MemoryImportance.Low, Base.AddHours(3), MemorySeeding.Vector(0f, 0f, 1f)));
		var       memory = NewMemory(store, NoOp, TimeProvider.System);

		var request            = new MemoryContracts.RecallRequest { Query = "aardvark" };
		var expectedContent    = "aardvark burrows deep underground";
		var expectedRetainedAt = Base.AddHours(1);

		// Act
		var response = await memory.RecallAsync(request);

		// Assert — the keyword isolates a1 alone, scored, and lean by default (no query id supplied,
		// so the service minted one).
		await Assert.That(response.QueryId).IsNotEqualTo("");
		await Assert.That(response.Memories.Count).IsEqualTo(1);

		var hit = response.Memories[0];

		await Assert.That(hit.BodyCase).IsEqualTo(MemoryContracts.RecallResponse.Types.RecalledMemory.BodyOneofCase.Lean);
		await Assert.That(hit.Full).IsNull();
		await Assert.That(hit.Score).IsGreaterThan(0);
		await Assert.That(hit.Lean.MemoryId).IsEqualTo("a1");
		await Assert.That(hit.Lean.Content).IsEqualTo(expectedContent);
		await Assert.That(hit.Lean.MemoryType).IsEqualTo(MemoryContracts.MemoryType.Fact);
		await Assert.That(hit.Lean.Importance).IsEqualTo(MemoryContracts.MemoryImportance.High);
		await Assert.That(hit.Lean.RetainedAt.ToDateTimeOffset()).IsEqualTo(expectedRetainedAt);
	}

	[Test]
	public async ValueTask recall_returns_full_memories_when_include_full_is_set() {
		// Arrange — one memory carrying the heavy fields lean drops (evidence, content-time window).
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources,
			new Row("b1", MemoryContracts.MemoryType.Fact, "flamingo stands gracefully on one leg", MemoryContracts.MemoryImportance.High, Base.AddHours(1), MemorySeeding.Vector(1f)) {
				Evidence      = SeedEvidenceBlobs(),
				ContentTimeStart = Base.AddHours(-24),
				ContentTimeEnd   = Base.AddHours(24),
			});
		var       memory = NewMemory(store, NoOp, TimeProvider.System);

		var request         = new MemoryContracts.RecallRequest { Query = "flamingo", IncludeFull = true };
		var expectedContent = "flamingo stands gracefully on one leg";

		// Act
		var response = await memory.RecallAsync(request);

		// Assert — the complete folded record rides on the hit, evidence and all; the lean arm is empty.
		await Assert.That(response.Memories.Count).IsEqualTo(1);

		var hit = response.Memories[0];

		await Assert.That(hit.BodyCase).IsEqualTo(MemoryContracts.RecallResponse.Types.RecalledMemory.BodyOneofCase.Full);
		await Assert.That(hit.Lean).IsNull();
		await Assert.That(hit.Score).IsGreaterThan(0);
		await Assert.That(hit.Full.MemoryId).IsEqualTo("b1");
		await Assert.That(hit.Full.Content).IsEqualTo(expectedContent);
		await Assert.That(hit.Full.Evidence.ToList()).IsEquivalentTo([SeedEvidence()]);
		await Assert.That(hit.Full.ContentTime!.PerceivedStart.ToDateTimeOffset()).IsEqualTo(Base.AddHours(-24));
	}

	[Test]
	public async ValueTask recall_never_surfaces_superseded_memories() {
		// Arrange — three memories all carrying "wombat"; the superseded one is hidden.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources,
			new Row("c1", MemoryContracts.MemoryType.Fact, "wombat digs a cozy burrow", MemoryContracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)),
			new Row("c2", MemoryContracts.MemoryType.Fact, "wombat mistaken hidden note", MemoryContracts.MemoryImportance.Normal, Base.AddHours(2), MemorySeeding.Vector(0f, 1f)),
			new Row("c3", MemoryContracts.MemoryType.Fact, "wombat obsolete replaced entry", MemoryContracts.MemoryImportance.Normal, Base.AddHours(3), MemorySeeding.Vector(0f, 0f, 1f)) {
				IsSuperseded = true,
				SupersededAt = Base.AddHours(4),
				SupersededBy = "c1",
			});
		var       memory = NewMemory(store, NoOp, TimeProvider.System);

		var request        = new MemoryContracts.RecallRequest { Query = "wombat" };
		var expectedVisible = new List<string> { "c1", "c2" };

		// Act
		var response = await memory.RecallAsync(request);

		// Assert — the living memories come back; the superseded one stays hidden.
		var ids = response.Memories.Select(m => m.Lean.MemoryId).ToList();

		await Assert.That(ids).IsEquivalentTo(expectedVisible, CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask recall_filters_by_tags() {
		// Arrange — both memories carry "salmon"; only d1 wears the project:rivers tag.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources,
			new Row("d1", MemoryContracts.MemoryType.Fact, "salmon swim upstream every year", MemoryContracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)) {
				Tags = ["project:rivers"],
			},
			new Row("d2", MemoryContracts.MemoryType.Fact, "salmon spawn in shallow gravel", MemoryContracts.MemoryImportance.Normal, Base.AddHours(2), MemorySeeding.Vector(0f, 1f)));
		var       memory = NewMemory(store, NoOp, TimeProvider.System);

		var request        = new MemoryContracts.RecallRequest { Query = "salmon" };
		request.Tags.Add(new MemoryContracts.Tag { Scope = "project", Value = "rivers" });
		var expectedTagged = new List<string> { "d1" };

		// Act
		var response = await memory.RecallAsync(request);

		// Assert — the untagged match is filtered out even though its content matches the query.
		var ids = response.Memories.Select(m => m.Lean.MemoryId).ToList();

		await Assert.That(ids).IsEquivalentTo(expectedTagged, CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask reclaim_returns_exact_ids_and_skips_unknown() {
		// Arrange — two stored memories; reclaim asks for both plus an id that was never stored.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources,
			new Row("e1", MemoryContracts.MemoryType.Fact, "kangaroo hops across the plains", MemoryContracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)),
			new Row("e2", MemoryContracts.MemoryType.Fact, "kangaroo mistaken claim", MemoryContracts.MemoryImportance.Normal, Base.AddHours(2), MemorySeeding.Vector(0f, 1f)));
		var       memory = NewMemory(store, NoOp, TimeProvider.System);

		var request = new MemoryContracts.ReclaimRequest();
		request.Ids.AddRange(["e1", "e2", "no-such-memory"]);
		var expectedReturned = new List<string> { "e1", "e2" };

		// Act
		var memories = await memory.ReclaimAsync(request).ToListAsync();

		// Assert — exactly the ids that exist; the id that doesn't is simply absent, never an error.
		var ids = memories.Select(m => m.MemoryId).Order().ToList();

		await Assert.That(ids).IsEquivalentTo(expectedReturned, CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask recollect_lists_by_type_and_sorts() {
		// Arrange — four memories of two types and distinct importances. Recollect scopes to FACT and
		// orders by importance descending, so only the three facts return, most-important first.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources,
			new Row("f1", MemoryContracts.MemoryType.Fact, "fact about caching", MemoryContracts.MemoryImportance.High, Base.AddHours(1), MemorySeeding.Vector(1f)) { LastAccessedAt = Base.AddHours(10) },
			new Row("f2", MemoryContracts.MemoryType.Fact, "fact about the checkpoint format", MemoryContracts.MemoryImportance.Critical, Base.AddHours(2), MemorySeeding.Vector(0f, 1f)) { LastAccessedAt = Base.AddHours(20) },
			new Row("f3", MemoryContracts.MemoryType.Preference, "prefers the projector rewritten in one pass", MemoryContracts.MemoryImportance.Normal, Base.AddHours(3), MemorySeeding.Vector(0f, 0f, 1f)) { LastAccessedAt = Base.AddHours(30) },
			new Row("f4", MemoryContracts.MemoryType.Fact, "fact about tags", MemoryContracts.MemoryImportance.Low, Base.AddHours(4), MemorySeeding.Vector(0f, 0f, 0f, 1f)) { LastAccessedAt = Base.AddHours(5) });
		var       memory = NewMemory(store, NoOp, TimeProvider.System);

		var request = new MemoryContracts.RecollectRequest {
			Sort      = MemoryContracts.RecollectSort.Importance,
			Direction = MemoryContracts.SortDirection.Descending,
		};
		request.Types_.Add(MemoryContracts.MemoryType.Fact);
		var expectedOrder = new List<string> { "f2", "f1", "f4" };

		// Act
		var memories = await memory.RecollectAsync(request).ToListAsync();

		// Assert — the preference is excluded, and the facts arrive critical → high → low.
		var ids = memories.Select(m => m.MemoryId).ToList();

		await Assert.That(ids).IsEquivalentTo(expectedOrder, CollectionOrdering.Matching);
	}

	#region ->> Test Infrastructure <<-

	static KontextMemory NewMemory(KontextMemoryDataStore store, AppendEvent append, TimeProvider clock, EmbeddingGenerator? embeddings = null) =>
		new(store, KeywordRetriever(store), append, clock, embeddings ?? new StubEmbeddings(), new KontextMemoryOptions());

	/// <summary>Records what reaches the log; the projector, not this service, applies it.</summary>
	static AppendEvent Capture(List<MemoryContracts.MemoriesRetained> appended) =>
		(evt, _) => {
			appended.Add((MemoryContracts.MemoriesRetained)evt);
			return Task.CompletedTask;
		};

	/// <summary>
	/// A unit vector at cosine 0.25 to <c>MemorySeeding.Vector(1f)</c>, which puts the pair at
	/// squared L2 1.5 — between the default MergeCeiling and AppendFloor.
	/// </summary>
	static float[] Ambiguous => MemorySeeding.Vector(0.25f, MathF.Sqrt(1f - 0.25f * 0.25f));

	/// <summary>One fixed vector for every text, so a test states the distance it wants exactly.</summary>
	sealed class FixedEmbeddings(float[] vector) : EmbeddingGenerator {
		public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
			IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default
		) =>
			Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
				values.Select(_ => new Embedding<float>(vector)).ToList()));

		public object? GetService(Type serviceType, object? serviceKey = null) => null;

		public void Dispose() { }
	}

	/// <summary>
	/// Deterministic vectors keyed off the text's hash — enough to keep the vector leg well-formed
	/// without paying an ONNX model load in a suite that ranks on keywords.
	/// </summary>
	sealed class StubEmbeddings : EmbeddingGenerator {
		public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
			IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default
		) {
			var embeddings = values
				.Select(value => {
					var vector = new float[KontextIndexConstants.VectorsDimension];
					vector[Math.Abs(value.GetHashCode()) % vector.Length] = 1f;
					return new Embedding<float>(vector);
				})
				.ToList();

			return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
		}

		public object? GetService(Type serviceType, object? serviceKey = null) => null;

		public void Dispose() { }
	}

	/// <summary>Discards the appended event, for the tests that assert on the response alone.</summary>
	static readonly AppendEvent NoOp = static (_, _) => Task.CompletedTask;

	/// <summary>A keyword-only pipeline over the store — recall here is raw BM25, so the seeded vectors never decide a result.</summary>
	static IKontextRetriever KeywordRetriever(KontextMemoryDataStore store) =>
		KontextRetriever.New().AddSearch(new KeywordSearch(store)).Build();

	static MemoryContracts.Evidence SeedEvidence() => new() { Memory = new() { Id = "cited-1" } };

	// evidence is a VARCHAR[] column: one canonical-JSON citation per element.
	static List<string> SeedEvidenceBlobs() => [JsonFormatter.Default.Format(SeedEvidence())];

	/// <summary>Creates the schema through <see cref="KontextMigrations"/> and seeds the given rows, then hands back a store over the same data sources.</summary>
	static async ValueTask<KontextMemoryDataStore> Seed(KontextDataSource dataSource, params Row[] rows) {
		// The schema component owns CREATE TABLE and every eager index (including the FTS INVERTED
		// index the keyword recall needs) — seeding only inserts rows.
		await MemorySeeding.CreateSchema(dataSource);

		// One multi-row INSERT: N tuples of sixteen parameters each, bound row by row in AddRow's
		// column order. Kept apart from the schema DDL because parameters don't prepare across a
		// multi-statement batch — the one justified exception to single-command batching.
		const string columns =
			"""
			INSERT INTO ldb.main.memories (
			  memory_id,
			  memory_type,
			  content,
			  importance,
			  tags,
			  reasoning,
			  evidence,
			  supersedes,
			  content_time_start,
			  content_time_end,
			  retained_at,
			  last_accessed_at,
			  is_superseded,
			  superseded_at,
			  superseded_by,
			  embedding)
			VALUES
			""";

		var tuple  = "(" + string.Join(", ", Enumerable.Repeat("?", 16)) + ")";
		var values = string.Join(",\n", Enumerable.Repeat(tuple, rows.Length));

		dataSource.Execute(connection => {
			using var insert = connection.CreateCommand();
			insert.CommandText = $"{columns}\n{values}";

			foreach (var row in rows)
				AddRow(insert, row);

			insert.ExecuteNonQuery();
		});

		return new(dataSource);
	}

	// Binds one VALUES tuple, in the INSERT's column order; null binds as NULL. Supersedes is
	// neutral here — these tests never read it.
	static void AddRow(DuckDBCommand command, Row row) {
		// Timestamps bind as Unix epoch milliseconds — the schema's BIGINT columns.
		object?[] values = [
			row.Id,
			(int)row.Type,
			row.Content,
			(int)row.Importance,
			row.Tags,
			row.Reasoning,
			row.Evidence,
			new List<string>(),                  // supersedes
			row.ContentTimeStart?.ToUnixTimeMilliseconds(),
			row.ContentTimeEnd?.ToUnixTimeMilliseconds(),
			row.RetainedAt.ToUnixTimeMilliseconds(),
			(row.LastAccessedAt ?? row.RetainedAt).ToUnixTimeMilliseconds(),
			row.IsSuperseded,
			row.SupersededAt?.ToUnixTimeMilliseconds(),
			row.SupersededBy,
			row.Embedding,
		];

		foreach (var value in values)
			command.Parameters.Add(new DuckDBParameter(value ?? DBNull.Value));
	}

	/// <summary>One seed row: the fields these tests set, with neutral defaults for the rest.</summary>
	sealed record Row(
		string Id,
		MemoryContracts.MemoryType Type,
		string Content,
		MemoryContracts.MemoryImportance Importance,
		DateTimeOffset RetainedAt,
		float[] Embedding
	) {
		public List<string>    Tags           { get; init; } = [];
		public string          Reasoning      { get; init; } = "";
		public List<string>    Evidence       { get; init; } = [];
		public DateTimeOffset? LastAccessedAt { get; init; }
		public bool            IsSuperseded   { get; init; }
		public DateTimeOffset? SupersededAt   { get; init; }
		public string          SupersededBy   { get; init; } = "";
		public DateTimeOffset? ContentTimeStart  { get; init; }
		public DateTimeOffset? ContentTimeEnd    { get; init; }
	}

	static KontextDataSource NewDataSources(string dir) => MemorySeeding.NewDataSources(dir);

	/// <summary>A unique temp directory owned by one test; deleted on dispose.</summary>
	sealed class TempDir : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontext-memory-tests", Guid.NewGuid().ToString("N"));

		public TempDir() => Directory.CreateDirectory(Path);

		public void Dispose() {
			try {
				if (Directory.Exists(Path))
					Directory.Delete(Path, recursive: true);
			} catch (IOException) {
				// Best-effort cleanup; a lingering native handle must not fail the test.
			} catch (UnauthorizedAccessException) {
				// Best-effort cleanup.
			}
		}
	}

	#endregion // Test Infrastructure
}
