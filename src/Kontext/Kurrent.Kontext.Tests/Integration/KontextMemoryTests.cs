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
/// projector-owned <see cref="KontextMemoryDataStore"/> read model. The write path is not built yet, so
/// each test seeds the memories table directly with SQL — exactly how the projector will write it —
/// and exercises the read-only surface the service exposes:
/// - retain appends one MemoriesRetained event per call and mints the ids; reflect still throws
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

		// The dataset must exist even with nothing in it: retain searches for neighbours before it
		// appends, so an unseeded store would fail the search rather than return none.
		await MemorySeeding.CreateSchema(dataSources);

		var appended = new List<Contracts.MemoriesRetained>();
		var clock    = new FakeTimeProvider(Base);
		var store    = new KontextMemoryDataStore(dataSources);
		var memory   = NewMemory(store, (evt, _) => {
			appended.Add((Contracts.MemoriesRetained)evt);
			return Task.CompletedTask;
		}, clock);

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
	public async ValueTask retain_reports_the_near_duplicate_it_is_about_to_create() {
		// Arrange — a store already holding the memory the caller is about to restate.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		var       store       = await Seed(dataSources,
			new Row("existing", Contracts.MemoryType.Fact, "the test runner lives at scripts/testing/test-runner.cs", Contracts.MemoryImportance.Normal, Base.AddHours(1), MemorySeeding.Vector(1f)),
			new Row("unrelated", Contracts.MemoryType.Fact, "penguins waddle across antarctic ice", Contracts.MemoryImportance.Normal, Base.AddHours(2), MemorySeeding.Vector(0f, 1f)));

		var memory  = NewMemory(store, NoOp, TimeProvider.System);
		var request = new Contracts.RetainRequest();

		request.Memories.Add(new Contracts.Memory {
			MemoryType = Contracts.MemoryType.Fact,
			Content    = "tests run only through scripts/testing/test-runner.cs",
		});

		// Act
		var response = await memory.RetainAsync(request);

		// Assert — the neighbour is reported so the caller can supersede instead of duplicating.
		// The server never merges or blocks: retain always stores, and the judgement stays with the
		// caller, because similarity cannot prove two memories are the same.
		var related = response.Results[0].Related;

		await Assert.That(related).IsNotEmpty();
		await Assert.That(related[0].Memory.MemoryId).IsEqualTo("existing");
		await Assert.That(related[0].Similarity).IsGreaterThan(0);
		await Assert.That(related[0].Memory.Content).IsEqualTo("the test runner lives at scripts/testing/test-runner.cs");
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

	static KontextMemory NewMemory(KontextMemoryDataStore store, AppendEvent append, TimeProvider clock) =>
		new(store, KeywordRetriever(store), append, clock, new StubEmbeddings(), new KontextMemoryOptions());

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

	/// <summary>A no-op append: the write path is not built, so nothing this service does emits events yet.</summary>
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
