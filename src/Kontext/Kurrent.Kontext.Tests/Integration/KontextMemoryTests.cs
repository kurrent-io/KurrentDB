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

		var appended = new List<Contracts.MemoriesRetained>();
		var clock    = new FakeTimeProvider(Base);
		var store    = new KontextMemoryDataStore(dataSources);
		var memory   = NewMemory(store, Capture(appended), clock);

		var request = new Contracts.RetainRequest();
		request.Memories.Add(new Contracts.Memory { MemoryType = Contracts.MemoryType.Fact, Content = "the suite runs via test-runner.cs" });
		request.Memories.Add(new Contracts.Memory { MemoryType = Contracts.MemoryType.Preference, Content = "prefers K&R braces" });

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
	public async ValueTask retain_noops_when_a_live_memory_is_already_byte_for_byte_this_one() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		const string content = "the test runner lives at scripts/testing/test-runner.cs";

		var repo  = new Contracts.Tag { Scope = "repo", Value = "kurrentdb" };
		var store = await Seed(dataSources,
			new Row("existing", Contracts.MemoryType.Fact, content, Contracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)) {
				Tags     = [KontextMemoryDataStore.EncodeTag(repo)],
				Evidence = SeedEvidenceBlobs(),
			});

		var appended = new List<Contracts.MemoriesRetained>();
		var memory   = NewMemory(store, Capture(appended), TimeProvider.System);
		var request  = new Contracts.RetainRequest();

		var incoming = new Contracts.Memory { MemoryType = Contracts.MemoryType.Fact, Content = content };
		incoming.Tags.Add(repo);
		incoming.Evidence.Add(SeedEvidence());
		request.Memories.Add(incoming);

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — an idempotency guard against a resend: same content, same tags, same evidence, so
		// there is nothing a second copy could carry that the first does not.
		await Assert.That(response.Results[0].Outcome).IsEqualTo(Contracts.RetainOutcome.Noop);
		await Assert.That(response.Results[0].MemoryId).IsEqualTo("existing");
		await Assert.That(appended).IsEmpty();
	}

	[Test]
	public async ValueTask retain_stores_the_same_content_again_when_the_tags_differ() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		const string content = "the test runner lives at scripts/testing/test-runner.cs";

		var repo    = new Contracts.Tag { Scope = "repo", Value = "kurrentdb" };
		var session = new Contracts.Tag { Scope = "session", Value = "9fb733bd" };

		var store = await Seed(dataSources,
			new Row("existing", Contracts.MemoryType.Fact, content, Contracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)) {
				Tags     = [KontextMemoryDataStore.EncodeTag(repo)],
				Evidence = SeedEvidenceBlobs(),
			});

		var appended = new List<Contracts.MemoriesRetained>();
		var memory   = NewMemory(store, Capture(appended), TimeProvider.System);
		var request  = new Contracts.RetainRequest();

		var incoming = new Contracts.Memory { MemoryType = Contracts.MemoryType.Fact, Content = content };
		incoming.Tags.Add(session);
		request.Memories.Add(incoming);

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — anything short of identical is stored AS SENT. Folding the stored memory's tags
		// and citations in would hand back a memory carrying labels the caller never chose, and the
		// caller cannot have "forgotten" to supersede something it did not know was there.
		await Assert.That(response.Results[0].Outcome).IsEqualTo(Contracts.RetainOutcome.Created);

		var written = appended[0].Memories[0].Memory;

		await Assert.That(written.Tags).IsEquivalentTo([session], CollectionOrdering.Any);
		await Assert.That(written.Evidence).IsEmpty();
		await Assert.That(written.Supersedes).IsEmpty();
	}

	[Test]
	public async ValueTask retain_stores_a_near_duplicate_without_asking_and_without_merging() {
		// Arrange — identical vectors, so the pair is as close as the engine can report. Under the
		// old design this was a merge; the server no longer has that opinion.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var store = await Seed(dataSources,
			new Row("existing", Contracts.MemoryType.Fact, "the projector checkpoints after the batch lands", Contracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)) {
				Evidence = SeedEvidenceBlobs(),
			});

		var appended = new List<Contracts.MemoriesRetained>();
		var memory   = NewMemory(store, Capture(appended), TimeProvider.System, new FixedEmbeddings(MemorySeeding.Vector(1f)));
		var request  = new Contracts.RetainRequest();

		request.Memories.Add(new Contracts.Memory {
			MemoryType = Contracts.MemoryType.Fact,
			Content    = "the projector checkpoints once the batch has landed",
		});

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — stored as sent. Nothing was superseded and no citation was inherited: at this
		// distance the two are plausibly the same claim, and plausibly is not good enough to rewrite
		// what the caller wrote. The curation pass folds it later, with the whole corpus in view.
		await Assert.That(response.Results[0].Outcome).IsEqualTo(Contracts.RetainOutcome.Created);

		var written = appended[0].Memories[0].Memory;

		await Assert.That(written.Supersedes).IsEmpty();
		await Assert.That(written.Evidence).IsEmpty();
	}

	[Test]
	public async ValueTask retain_searches_for_nothing_when_no_neighbours_were_asked_for() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var store = await Seed(dataSources,
			new Row("existing", Contracts.MemoryType.Fact, "a colleague reported the outage during standup", Contracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)));

		var embeddings = new CountingEmbeddings(MemorySeeding.Vector(1f));
		var memory     = NewMemory(store, NoOp, TimeProvider.System, embeddings);
		var request    = new Contracts.RetainRequest();

		request.Memories.Add(new Contracts.Memory {
			MemoryType = Contracts.MemoryType.Fact,
			Content    = "someone mentioned the downtime at the morning meeting",
		});

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — the embedding exists ONLY to answer the neighbour search; the projector computes
		// the vector the store keeps. Nobody asked, so retain must not touch a model at all.
		await Assert.That(response.Results[0].Neighbours).IsEmpty();
		await Assert.That(embeddings.Calls).IsEqualTo(0);
	}

	[Test]
	public async ValueTask retain_reports_the_nearest_memories_when_asked_for_them() {
		// Arrange — the query vector sits at cosine 0.25 to "far" and cosine 0.968 to "near", so the
		// squared L2 distances are 2 - 2(0.25) = 1.5 and 2 - 2(0.968) = 0.0635.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var store = await Seed(dataSources,
			new Row("far", Contracts.MemoryType.Fact, "a colleague reported the outage during standup", Contracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)),
			new Row("near", Contracts.MemoryType.Fact, "someone mentioned the downtime at the morning meeting", Contracts.MemoryImportance.Normal, Base.AddHours(2), MemorySeeding.Vector(0f, 1f)));

		var appended = new List<Contracts.MemoriesRetained>();
		var memory   = NewMemory(store, Capture(appended), TimeProvider.System, new FixedEmbeddings(Cosine25));
		var request  = new Contracts.RetainRequest { Neighbours = 2 };

		request.Memories.Add(new Contracts.Memory {
			MemoryType = Contracts.MemoryType.Fact,
			Content    = "the outage came up in this morning's standup",
		});

		var expectedOrder = new[] { "near", "far" };

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — the memory was stored regardless; the neighbours are a report, nearest first, and
		// each carries the raw distance rather than a score normalised across this one search.
		await Assert.That(response.Results[0].Outcome).IsEqualTo(Contracts.RetainOutcome.Created);
		await Assert.That(appended[0].Memories.Count).IsEqualTo(1);

		var neighbours = response.Results[0].Neighbours;

		await Assert.That(neighbours.Select(n => n.Memory.MemoryId)).IsEquivalentTo(expectedOrder, CollectionOrdering.Matching);
		await Assert.That(neighbours[0].Distance).IsEqualTo(0.0635).Within(1e-3);
		await Assert.That(neighbours[1].Distance).IsEqualTo(1.5).Within(1e-3);
	}

	[Test]
	public async ValueTask retain_reports_no_neighbours_for_a_memory_it_did_not_write() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		const string content = "the test runner lives at scripts/testing/test-runner.cs";

		var store = await Seed(dataSources,
			new Row("existing", Contracts.MemoryType.Fact, content, Contracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)),
			new Row("other", Contracts.MemoryType.Fact, "penguins waddle across antarctic ice", Contracts.MemoryImportance.Normal, Base.AddHours(2), MemorySeeding.Vector(0f, 1f)));

		var memory  = NewMemory(store, NoOp, TimeProvider.System, new FixedEmbeddings(MemorySeeding.Vector(1f)));
		var request = new Contracts.RetainRequest { Neighbours = 3 };

		request.Memories.Add(new Contracts.Memory { MemoryType = Contracts.MemoryType.Fact, Content = content });

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — a NOOP resolved to a memory that was already there, so there is no "what did I
		// store next to" to answer.
		await Assert.That(response.Results[0].Outcome).IsEqualTo(Contracts.RetainOutcome.Noop);
		await Assert.That(response.Results[0].Neighbours).IsEmpty();
	}

	[Test]
	public async ValueTask retain_keeps_results_aligned_when_a_batch_mixes_a_noop_with_a_create() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		const string known = "the test runner lives at scripts/testing/test-runner.cs";

		var store = await Seed(dataSources,
			new Row("existing", Contracts.MemoryType.Fact, known, Contracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)));

		var appended = new List<Contracts.MemoriesRetained>();
		var memory   = NewMemory(store, Capture(appended), TimeProvider.System, new FixedEmbeddings(MemorySeeding.Vector(1f)));
		var request  = new Contracts.RetainRequest { Neighbours = 2 };

		request.Memories.Add(new Contracts.Memory { MemoryType = Contracts.MemoryType.Fact, Content = known });
		request.Memories.Add(new Contracts.Memory { MemoryType = Contracts.MemoryType.Fact, Content = "gossip timeouts default to two seconds" });

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — results[i] is the memory sent at memories[i] even when the outcomes differ, and
		// only the memory that was actually written gets neighbours. The batch walks the request,
		// the results and the embeddings in lockstep, so a skipped write must not shift the rest.
		await Assert.That(response.Results.Count).IsEqualTo(2);

		await Assert.That(response.Results[0].Outcome).IsEqualTo(Contracts.RetainOutcome.Noop);
		await Assert.That(response.Results[0].MemoryId).IsEqualTo("existing");
		await Assert.That(response.Results[0].Neighbours).IsEmpty();

		await Assert.That(response.Results[1].Outcome).IsEqualTo(Contracts.RetainOutcome.Created);
		await Assert.That(response.Results[1].Neighbours).IsNotEmpty();

		// One memory reached the log, and it is the one that was created.
		await Assert.That(appended[0].Memories.Count).IsEqualTo(1);
		await Assert.That(appended[0].Memories[0].MemoryId).IsEqualTo(response.Results[1].MemoryId);
		await Assert.That(appended[0].Memories[0].Memory.Content).IsEqualTo("gossip timeouts default to two seconds");
	}

	[Test]
	public async ValueTask retain_carries_the_supersedes_the_caller_sent_into_the_event() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var store = await Seed(dataSources,
			new Row("outdated", Contracts.MemoryType.Fact, "Sergio leads DevEx", Contracts.MemoryImportance.High, Base.AddHours(1), MemorySeeding.Vector(1f)));

		var appended = new List<Contracts.MemoriesRetained>();
		var memory   = NewMemory(store, Capture(appended), TimeProvider.System);
		var request  = new Contracts.RetainRequest();

		var incoming = new Contracts.Memory { MemoryType = Contracts.MemoryType.Fact, Content = "Sergio is CTO" };
		incoming.Supersedes.Add("outdated");
		request.Memories.Add(incoming);

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — supersession is the caller's to express and the server's only job is to carry it
		// through untouched. This is the whole correction mechanism: there is no update and no
		// delete, so a `supersedes` the server dropped would silently lose the correction.
		await Assert.That(response.Results[0].Outcome).IsEqualTo(Contracts.RetainOutcome.Created);
		await Assert.That(appended[0].Memories[0].Memory.Supersedes).IsEquivalentTo(["outdated"]);
	}

	[Test]
	public async ValueTask retain_stores_the_same_content_again_when_the_evidence_differs() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		const string content = "the test runner lives at scripts/testing/test-runner.cs";

		var repo  = new Contracts.Tag { Scope = "repo", Value = "kurrentdb" };
		var store = await Seed(dataSources,
			new Row("existing", Contracts.MemoryType.Fact, content, Contracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)) {
				Tags     = [KontextMemoryDataStore.EncodeTag(repo)],
				Evidence = SeedEvidenceBlobs(),
			});

		var appended = new List<Contracts.MemoriesRetained>();
		var memory   = NewMemory(store, Capture(appended), TimeProvider.System);
		var request  = new Contracts.RetainRequest();

		// Same content, same tags, DIFFERENT citation — new support for a claim already held.
		var incoming = new Contracts.Memory { MemoryType = Contracts.MemoryType.Fact, Content = content };
		incoming.Tags.Add(repo);
		incoming.Evidence.Add(new Contracts.Evidence { Memory = new() { Id = "cited-2" } });
		request.Memories.Add(incoming);

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — identical means content AND tags AND evidence. A second citation is exactly the
		// case worth keeping, so it must not be swallowed by the idempotency guard.
		await Assert.That(response.Results[0].Outcome).IsEqualTo(Contracts.RetainOutcome.Created);
		await Assert.That(appended[0].Memories[0].Memory.Evidence.Count).IsEqualTo(1);
	}

	[Test]
	public async ValueTask retain_never_reports_more_neighbours_than_the_configured_maximum() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var crowd = Enumerable.Range(0, 12)
			.Select(i => new Row($"m{i}", Contracts.MemoryType.Fact, $"note {i} about checkpoint bookkeeping", Contracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)))
			.ToArray();

		var store = await Seed(dataSources, crowd);

		var options = new KontextMemoryOptions();
		var memory  = NewMemory(store, NoOp, TimeProvider.System, new FixedEmbeddings(MemorySeeding.Vector(1f)), options);
		var request = new Contracts.RetainRequest { Neighbours = 100 };

		request.Memories.Add(new Contracts.Memory { MemoryType = Contracts.MemoryType.Fact, Content = "checkpoint bookkeeping runs on a schedule" });

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — the cap is the server's, not the caller's. Each neighbour rides back as a whole
		// LeanMemory, so an unbounded ask would spend the caller's context on the server's behalf.
		await Assert.That(response.Results[0].Neighbours.Count).IsEqualTo(options.MaxNeighbours);
	}

	[Test]
	public async ValueTask retain_marks_a_neighbour_the_keyword_leg_also_matched() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		var store = await Seed(dataSources,
			new Row("lexical", Contracts.MemoryType.Fact, "the projector checkpoints after the batch lands", Contracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)));

		// The schema creates content_fts on the empty table, so seeded rows land in the unindexed
		// tail where lance_fts returns arbitrary rows by scan arrival. Without this rebuild the
		// keyword leg is not being measured at all.
		RebuildContentFts(dataSources);

		var memory  = NewMemory(store, NoOp, TimeProvider.System, new FixedEmbeddings(MemorySeeding.Vector(1f)));
		var request = new Contracts.RetainRequest { Neighbours = 2 };

		request.Memories.Add(new Contracts.Memory {
			MemoryType = Contracts.MemoryType.Fact,
			Content    = "the projector checkpoints once the batch has landed",
		});

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — the second half of the signal. A keyword match at a low distance is a restatement
		// in mostly the same words; a low distance alone is a reword.
		var neighbour = response.Results[0].Neighbours.Single(n => n.Memory.MemoryId == "lexical");

		await Assert.That(neighbour.KeywordMatch).IsTrue();
	}

	[Test]
	public async ValueTask retain_reports_no_neighbours_when_there_is_nothing_stored_yet() {
		// Arrange — the very first retain against a fresh store, which is when the dataset holds no
		// rows for either search leg to read.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await MemorySeeding.CreateSchema(dataSources);

		var appended = new List<Contracts.MemoriesRetained>();
		var store    = new KontextMemoryDataStore(dataSources);
		var memory   = NewMemory(store, Capture(appended), TimeProvider.System, new FixedEmbeddings(MemorySeeding.Vector(1f)));
		var request  = new Contracts.RetainRequest { Neighbours = 3 };

		request.Memories.Add(new Contracts.Memory { MemoryType = Contracts.MemoryType.Fact, Content = "the first thing I ever learned" });

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — asking for neighbours must not make the first write fail. Nothing to report is a
		// result, not an error.
		await Assert.That(response.Results[0].Outcome).IsEqualTo(Contracts.RetainOutcome.Created);
		await Assert.That(response.Results[0].Neighbours).IsEmpty();
		await Assert.That(appended[0].Memories.Count).IsEqualTo(1);
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
			new Row("a1", Contracts.MemoryType.Fact, "aardvark burrows deep underground", Contracts.MemoryImportance.High, Base.AddHours(1), MemorySeeding.Vector(1f)),
			new Row("a2", Contracts.MemoryType.Fact, "penguins waddle across antarctic ice", Contracts.MemoryImportance.Normal, Base.AddHours(2), MemorySeeding.Vector(0f, 1f)),
			new Row("a3", Contracts.MemoryType.Fact, "giraffes browse the tallest acacia leaves", Contracts.MemoryImportance.Low, Base.AddHours(3), MemorySeeding.Vector(0f, 0f, 1f)));
		var       memory = NewMemory(store, NoOp, TimeProvider.System);

		var request            = new Contracts.RecallRequest { Query = "aardvark" };
		var expectedContent    = "aardvark burrows deep underground";
		var expectedRetainedAt = Base.AddHours(1);

		// Act
		var response = await memory.RecallAsync(request);

		// Assert — the keyword isolates a1 alone, scored, and lean by default (no query id supplied,
		// so the service minted one).
		await Assert.That(response.QueryId).IsNotEqualTo("");
		await Assert.That(response.Memories.Count).IsEqualTo(1);

		var hit = response.Memories[0];

		await Assert.That(hit.BodyCase).IsEqualTo(Contracts.RecallResponse.Types.RecalledMemory.BodyOneofCase.Lean);
		await Assert.That(hit.Full).IsNull();
		await Assert.That(hit.Score).IsGreaterThan(0);
		await Assert.That(hit.Lean.MemoryId).IsEqualTo("a1");
		await Assert.That(hit.Lean.Content).IsEqualTo(expectedContent);
		await Assert.That(hit.Lean.MemoryType).IsEqualTo(Contracts.MemoryType.Fact);
		await Assert.That(hit.Lean.Importance).IsEqualTo(Contracts.MemoryImportance.High);
		await Assert.That(hit.Lean.RetainedAt.ToDateTimeOffset()).IsEqualTo(expectedRetainedAt);
	}

	[Test]
	public async ValueTask recall_returns_full_memories_when_include_full_is_set() {
		// Arrange — one memory carrying the heavy fields lean drops (evidence, content-time window).
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources,
			new Row("b1", Contracts.MemoryType.Fact, "flamingo stands gracefully on one leg", Contracts.MemoryImportance.High, Base.AddHours(1), MemorySeeding.Vector(1f)) {
				Evidence      = SeedEvidenceBlobs(),
				ContentTimeStart = Base.AddHours(-24),
				ContentTimeEnd   = Base.AddHours(24),
			});
		var       memory = NewMemory(store, NoOp, TimeProvider.System);

		var request         = new Contracts.RecallRequest { Query = "flamingo", IncludeFull = true };
		var expectedContent = "flamingo stands gracefully on one leg";

		// Act
		var response = await memory.RecallAsync(request);

		// Assert — the complete folded record rides on the hit, evidence and all; the lean arm is empty.
		await Assert.That(response.Memories.Count).IsEqualTo(1);

		var hit = response.Memories[0];

		await Assert.That(hit.BodyCase).IsEqualTo(Contracts.RecallResponse.Types.RecalledMemory.BodyOneofCase.Full);
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
			new Row("c1", Contracts.MemoryType.Fact, "wombat digs a cozy burrow", Contracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)),
			new Row("c2", Contracts.MemoryType.Fact, "wombat mistaken hidden note", Contracts.MemoryImportance.Normal, Base.AddHours(2), MemorySeeding.Vector(0f, 1f)),
			new Row("c3", Contracts.MemoryType.Fact, "wombat obsolete replaced entry", Contracts.MemoryImportance.Normal, Base.AddHours(3), MemorySeeding.Vector(0f, 0f, 1f)) {
				IsSuperseded = true,
				SupersededAt = Base.AddHours(4),
				SupersededBy = "c1",
			});
		var       memory = NewMemory(store, NoOp, TimeProvider.System);

		var request        = new Contracts.RecallRequest { Query = "wombat" };
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
			new Row("d1", Contracts.MemoryType.Fact, "salmon swim upstream every year", Contracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)) {
				Tags = ["project:rivers"],
			},
			new Row("d2", Contracts.MemoryType.Fact, "salmon spawn in shallow gravel", Contracts.MemoryImportance.Normal, Base.AddHours(2), MemorySeeding.Vector(0f, 1f)));
		var       memory = NewMemory(store, NoOp, TimeProvider.System);

		var request        = new Contracts.RecallRequest { Query = "salmon" };
		request.Tags.Add(new Contracts.Tag { Scope = "project", Value = "rivers" });
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
			new Row("e1", Contracts.MemoryType.Fact, "kangaroo hops across the plains", Contracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)),
			new Row("e2", Contracts.MemoryType.Fact, "kangaroo mistaken claim", Contracts.MemoryImportance.Normal, Base.AddHours(2), MemorySeeding.Vector(0f, 1f)));
		var       memory = NewMemory(store, NoOp, TimeProvider.System);

		var request = new Contracts.ReclaimRequest();
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
			new Row("f1", Contracts.MemoryType.Fact, "fact about caching", Contracts.MemoryImportance.High, Base.AddHours(1), MemorySeeding.Vector(1f)) { LastAccessedAt = Base.AddHours(10) },
			new Row("f2", Contracts.MemoryType.Fact, "fact about the checkpoint format", Contracts.MemoryImportance.Critical, Base.AddHours(2), MemorySeeding.Vector(0f, 1f)) { LastAccessedAt = Base.AddHours(20) },
			new Row("f3", Contracts.MemoryType.Preference, "prefers the projector rewritten in one pass", Contracts.MemoryImportance.Normal, Base.AddHours(3), MemorySeeding.Vector(0f, 0f, 1f)) { LastAccessedAt = Base.AddHours(30) },
			new Row("f4", Contracts.MemoryType.Fact, "fact about tags", Contracts.MemoryImportance.Low, Base.AddHours(4), MemorySeeding.Vector(0f, 0f, 0f, 1f)) { LastAccessedAt = Base.AddHours(5) });
		var       memory = NewMemory(store, NoOp, TimeProvider.System);

		var request = new Contracts.RecollectRequest {
			Sort      = Contracts.RecollectSort.Importance,
			Direction = Contracts.SortDirection.Descending,
		};
		request.Types_.Add(Contracts.MemoryType.Fact);
		var expectedOrder = new List<string> { "f2", "f1", "f4" };

		// Act
		var memories = await memory.RecollectAsync(request).ToListAsync();

		// Assert — the preference is excluded, and the facts arrive critical → high → low.
		var ids = memories.Select(m => m.MemoryId).ToList();

		await Assert.That(ids).IsEquivalentTo(expectedOrder, CollectionOrdering.Matching);
	}

	#region ->> Test Infrastructure <<-

	static KontextMemory NewMemory(
		KontextMemoryDataStore store,
		AppendEvent append,
		TimeProvider clock,
		EmbeddingGenerator? embeddings = null,
		KontextMemoryOptions? options = null
	) =>
		new(store, KeywordRetriever(store), append, clock, embeddings ?? new StubEmbeddings(), options ?? new KontextMemoryOptions());

	/// <summary>
	/// Rebuilds the FTS index over the seeded rows. The schema creates it on the empty table, so
	/// every seeded row otherwise lands in the unindexed tail, where lance_fts returns the first k
	/// rows by scan arrival instead of the top k by score.
	/// </summary>
	static void RebuildContentFts(KontextDataSource dataSources) =>
		dataSources.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText =
				"""
				CREATE INDEX content_fts ON ldb.main.memories (content) USING INVERTED
				WITH (replace = true, base_tokenizer = 'simple', language = 'English', stem = true);
				""";
			command.ExecuteNonQuery();
		});

	/// <summary>Records what reaches the log; the projector, not this service, applies it.</summary>
	static AppendEvent Capture(List<Contracts.MemoriesRetained> appended) =>
		(evt, _) => {
			appended.Add((Contracts.MemoriesRetained)evt);
			return Task.CompletedTask;
		};

	/// <summary>
	/// A unit vector at cosine 0.25 to <c>Vector(1f)</c> and cosine 0.968 to <c>Vector(0f, 1f)</c>,
	/// putting the two pairs at squared L2 1.5 and 0.0635.
	/// </summary>
	static float[] Cosine25 => MemorySeeding.Vector(0.25f, MathF.Sqrt(1f - 0.25f * 0.25f));

	/// <summary>One fixed vector for every text, so a test states the distance it wants exactly.</summary>
	class FixedEmbeddings(float[] vector) : EmbeddingGenerator {
		public virtual Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
			IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default
		) =>
			Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
				values.Select(_ => new Embedding<float>(vector)).ToList()));

		public object? GetService(Type serviceType, object? serviceKey = null) => null;

		public void Dispose() { }
	}

	/// <summary>Counts the calls, so "retain does not touch a model" is asserted rather than assumed.</summary>
	sealed class CountingEmbeddings(float[] vector) : FixedEmbeddings(vector) {
		public int Calls { get; private set; }

		public override Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
			IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default
		) {
			Calls++;
			return base.GenerateAsync(values, options, cancellationToken);
		}
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

	static Contracts.Evidence SeedEvidence() => new() { Memory = new() { Id = "cited-1" } };

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
		Contracts.MemoryType Type,
		string Content,
		Contracts.MemoryImportance Importance,
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
