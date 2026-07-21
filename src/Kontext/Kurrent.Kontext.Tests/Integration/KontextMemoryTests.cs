// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Google.Protobuf;
using TUnit.Assertions.Enums;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;

namespace Kurrent.Kontext.Tests;

/// <summary>
/// Behavioural tests for <see cref="KontextMemory"/> against a REAL DuckDB + Lance engine, over the
/// projector-owned <see cref="KontextDataStore"/> read model. The write path is not built yet, so
/// each test seeds the memories table directly with SQL — exactly how the projector will write it —
/// and exercises the read-only surface the service exposes:
/// - the three write operations (retain, retract, reflect) throw NotImplementedException
/// - recall is keyword-only BM25 search, lean by default, and never surfaces hidden memories
/// - reclaim is an exact-id passthrough that returns retracted memories too
/// - recollect lists by type/tag with a sort
///
/// Embeddings are seeded as literal 4-dim vectors so the table is well-formed; recall here is
/// keyword-only, so the vectors never decide a result.
/// </summary>
[Category("Integration")]
public class KontextMemoryTests {
	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask retain_throws_not_implemented() {
		// Arrange
		using var dir    = new TempDir();
		using var pool   = NewPool(dir.Path);
		var       memory = new KontextMemory(new KontextDataStore(pool), NoOp);

		// Act + Assert — the write path lands in the read model via the log, not through this service.
		await Assert.That(async () => await memory.RetainAsync(new())).Throws<NotImplementedException>();
	}

	[Test]
	public async ValueTask retract_throws_not_implemented() {
		// Arrange
		using var dir    = new TempDir();
		using var pool   = NewPool(dir.Path);
		var       memory = new KontextMemory(new KontextDataStore(pool), NoOp);

		// Act + Assert
		await Assert.That(async () => await memory.RetractAsync(new())).Throws<NotImplementedException>();
	}

	[Test]
	public async ValueTask reflect_throws_not_implemented() {
		// Arrange
		using var dir    = new TempDir();
		using var pool   = NewPool(dir.Path);
		var       memory = new KontextMemory(new KontextDataStore(pool), NoOp);

		// Act + Assert
		await Assert.That(async () => await memory.ReflectAsync(new())).Throws<NotImplementedException>();
	}

	[Test]
	public async ValueTask recall_finds_seeded_memories_by_keywords() {
		// Arrange — three memories with fully distinct vocabularies; only a1 carries "aardvark".
		using var dir    = new TempDir();
		using var pool   = NewPool(dir.Path);
		var       store  = await Seed(pool,
			new Row("a1", Contracts.MemoryType.Observation, "aardvark burrows deep underground", Contracts.MemoryImportance.High, Base.AddHours(1), [1f, 0f, 0f, 0f]),
			new Row("a2", Contracts.MemoryType.Fact, "penguins waddle across antarctic ice", Contracts.MemoryImportance.Normal, Base.AddHours(2), [0f, 1f, 0f, 0f]),
			new Row("a3", Contracts.MemoryType.Fact, "giraffes browse the tallest acacia leaves", Contracts.MemoryImportance.Low, Base.AddHours(3), [0f, 0f, 1f, 0f]));
		var       memory = new KontextMemory(store, NoOp);

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
		await Assert.That(hit.Lean.MemoryType).IsEqualTo(Contracts.MemoryType.Observation);
		await Assert.That(hit.Lean.Importance).IsEqualTo(Contracts.MemoryImportance.High);
		await Assert.That(hit.Lean.RetainedAt.ToDateTimeOffset()).IsEqualTo(expectedRetainedAt);
	}

	[Test]
	public async ValueTask recall_returns_full_memories_when_include_full_is_set() {
		// Arrange — one memory carrying the heavy fields lean drops (evidence, validity window).
		using var dir    = new TempDir();
		using var pool   = NewPool(dir.Path);
		var       store  = await Seed(pool,
			new Row("b1", Contracts.MemoryType.Fact, "flamingo stands gracefully on one leg", Contracts.MemoryImportance.High, Base.AddHours(1), [1f, 0f, 0f, 0f]) {
				Evidence      = SeedEvidence().ToByteArray(),
				ValidityStart = Base.AddHours(-24),
				ValidityEnd   = Base.AddHours(24),
			});
		var       memory = new KontextMemory(store, NoOp);

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
		await Assert.That(hit.Full.Evidence).IsEqualTo(SeedEvidence());
		await Assert.That(hit.Full.Validity!.PerceivedStart.ToDateTimeOffset()).IsEqualTo(Base.AddHours(-24));
	}

	[Test]
	public async ValueTask recall_never_surfaces_retracted_or_superseded_memories() {
		// Arrange — three memories all carrying "wombat"; two are hidden (one retracted, one superseded).
		using var dir    = new TempDir();
		using var pool   = NewPool(dir.Path);
		var       store  = await Seed(pool,
			new Row("c1", Contracts.MemoryType.Observation, "wombat digs a cozy burrow", Contracts.MemoryImportance.Normal, Base.AddHours(1), [1f, 0f, 0f, 0f]),
			new Row("c2", Contracts.MemoryType.Observation, "wombat mistaken hidden note", Contracts.MemoryImportance.Normal, Base.AddHours(2), [0f, 1f, 0f, 0f]) {
				IsRetracted = true,
				RetractedAt = Base.AddHours(5),
			},
			new Row("c3", Contracts.MemoryType.Observation, "wombat obsolete replaced entry", Contracts.MemoryImportance.Normal, Base.AddHours(3), [0f, 0f, 1f, 0f]) {
				IsSuperseded = true,
				SupersededAt = Base.AddHours(4),
				SupersededBy = "c1",
			});
		var       memory = new KontextMemory(store, NoOp);

		var request        = new Contracts.RecallRequest { Query = "wombat" };
		var expectedVisible = new List<string> { "c1" };

		// Act
		var response = await memory.RecallAsync(request);

		// Assert — only the living memory comes back; the retracted and superseded ones stay hidden.
		var ids = response.Memories.Select(m => m.Lean.MemoryId).ToList();

		await Assert.That(ids).IsEquivalentTo(expectedVisible, CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask recall_filters_by_tags() {
		// Arrange — both memories carry "salmon"; only d1 wears the project:rivers tag.
		using var dir    = new TempDir();
		using var pool   = NewPool(dir.Path);
		var       store  = await Seed(pool,
			new Row("d1", Contracts.MemoryType.Fact, "salmon swim upstream every year", Contracts.MemoryImportance.Normal, Base.AddHours(1), [1f, 0f, 0f, 0f]) {
				Tags = ["project:rivers"],
			},
			new Row("d2", Contracts.MemoryType.Fact, "salmon spawn in shallow gravel", Contracts.MemoryImportance.Normal, Base.AddHours(2), [0f, 1f, 0f, 0f]));
		var       memory = new KontextMemory(store, NoOp);

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
	public async ValueTask reclaim_returns_exact_ids_including_retracted() {
		// Arrange — a living memory and a retracted one; reclaim asks for both plus a stranger.
		using var dir    = new TempDir();
		using var pool   = NewPool(dir.Path);
		var       store  = await Seed(pool,
			new Row("e1", Contracts.MemoryType.Observation, "kangaroo hops across the plains", Contracts.MemoryImportance.Normal, Base.AddHours(1), [1f, 0f, 0f, 0f]),
			new Row("e2", Contracts.MemoryType.Observation, "kangaroo mistaken claim", Contracts.MemoryImportance.Normal, Base.AddHours(2), [0f, 1f, 0f, 0f]) {
				IsRetracted = true,
				RetractedAt = Base.AddHours(5),
			});
		var       memory = new KontextMemory(store, NoOp);

		var request = new Contracts.ReclaimRequest();
		request.Ids.AddRange(["e1", "e2", "no-such-memory"]);
		var expectedReturned = new List<string> { "e1", "e2" };

		// Act
		var memories = await memory.ReclaimAsync(request).ToListAsync();

		// Assert — exact ids, retracted included; the id that doesn't exist is simply absent.
		var ids = memories.Select(m => m.MemoryId).Order().ToList();

		await Assert.That(ids).IsEquivalentTo(expectedReturned, CollectionOrdering.Matching);
		await Assert.That(memories.Single(m => m.MemoryId == "e2").RetractedAt).IsNotNull();
	}

	[Test]
	public async ValueTask recollect_lists_by_type_and_sorts() {
		// Arrange — four memories of two types and distinct importances. Recollect scopes to FACT and
		// orders by importance descending, so only the three facts return, most-important first.
		using var dir    = new TempDir();
		using var pool   = NewPool(dir.Path);
		var       store  = await Seed(pool,
			new Row("f1", Contracts.MemoryType.Fact, "fact about caching", Contracts.MemoryImportance.High, Base.AddHours(1), [1f, 0f, 0f, 0f]) { LastAccessedAt = Base.AddHours(10) },
			new Row("f2", Contracts.MemoryType.Fact, "fact about the checkpoint format", Contracts.MemoryImportance.Critical, Base.AddHours(2), [0f, 1f, 0f, 0f]) { LastAccessedAt = Base.AddHours(20) },
			new Row("f3", Contracts.MemoryType.Plan, "plan to rewrite the projector", Contracts.MemoryImportance.Normal, Base.AddHours(3), [0f, 0f, 1f, 0f]) { LastAccessedAt = Base.AddHours(30) },
			new Row("f4", Contracts.MemoryType.Fact, "fact about tags", Contracts.MemoryImportance.Low, Base.AddHours(4), [0f, 0f, 0f, 1f]) { LastAccessedAt = Base.AddHours(5) });
		var       memory = new KontextMemory(store, NoOp);

		var request = new Contracts.RecollectRequest {
			Sort      = Contracts.RecollectSort.Importance,
			Direction = Contracts.SortDirection.Descending,
		};
		request.Types_.Add(Contracts.MemoryType.Fact);
		var expectedOrder = new List<string> { "f2", "f1", "f4" };

		// Act
		var memories = await memory.RecollectAsync(request).ToListAsync();

		// Assert — the plan is excluded, and the facts arrive critical → high → low.
		var ids = memories.Select(m => m.MemoryId).ToList();

		await Assert.That(ids).IsEquivalentTo(expectedOrder, CollectionOrdering.Matching);
	}

	#region ->> Test Infrastructure <<-

	/// <summary>A no-op append: the write path is not built, so nothing this service does emits events yet.</summary>
	static readonly AppendEvent NoOp = static (_, _) => Task.CompletedTask;

	static Contracts.Evidence SeedEvidence() {
		var evidence = new Contracts.Evidence();
		evidence.Citations.Add(new Contracts.Evidence.Types.Citation { Memory = new() { Id = "cited-1" } });
		return evidence;
	}

	/// <summary>Creates the schema through <see cref="KontextSchema"/> and seeds the given rows, then hands back a store over the same pool.</summary>
	static async ValueTask<KontextDataStore> Seed(KontextConnectionPool pool, params Row[] rows) {
		// The schema component owns CREATE TABLE and every eager index (including the FTS INVERTED
		// index the keyword recall needs) — seeding only inserts rows.
		await NewSchema(pool).CreateAsync();

		// One multi-row INSERT: N tuples of nineteen parameters each, bound row by row in AddRow's
		// column order. Kept apart from the schema DDL because parameters don't prepare across a
		// multi-statement batch — the one justified exception to single-command batching.
		const string columns =
			"""
			INSERT INTO ldb.main.memories (
			  memory_id,
			  memory_type,
			  content,
			  importance,
			  sentiment,
			  urgency,
			  tags,
			  evidence,
			  supersedes,
			  validity_start,
			  validity_end,
			  retained_at,
			  last_accessed_at,
			  is_retracted,
			  retracted_at,
			  is_superseded,
			  superseded_at,
			  superseded_by,
			  embedding)
			VALUES
			""";

		var tuple  = "(" + string.Join(", ", Enumerable.Repeat("?", 19)) + ")";
		var values = string.Join(",\n", Enumerable.Repeat(tuple, rows.Length));

		using (pool.Rent(out var connection)) {
			using var insert = connection.CreateCommand();
			insert.CommandText = $"{columns}\n{values}";

			foreach (var row in rows)
				AddRow(insert, row);

			insert.ExecuteNonQuery();
		}

		return new(pool);
	}

	// Binds one VALUES tuple, in the INSERT's column order; null binds as NULL. Sentiment, urgency
	// and supersedes are neutral here — these tests never read them.
	static void AddRow(DuckDBCommand command, Row row) {
		object?[] values = [
			row.Id,
			(int)row.Type,
			row.Content,
			(int)row.Importance,
			0,                                   // sentiment
			0,                                   // urgency
			row.Tags,
			row.Evidence,
			new List<string>(),                  // supersedes
			row.ValidityStart,
			row.ValidityEnd,
			row.RetainedAt,
			row.LastAccessedAt ?? row.RetainedAt,
			row.IsRetracted,
			row.RetractedAt,
			row.IsSuperseded,
			row.SupersededAt,
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
		public byte[]          Evidence       { get; init; } = [];
		public DateTimeOffset? LastAccessedAt { get; init; }
		public bool            IsRetracted    { get; init; }
		public DateTimeOffset? RetractedAt    { get; init; }
		public bool            IsSuperseded   { get; init; }
		public DateTimeOffset? SupersededAt   { get; init; }
		public string          SupersededBy   { get; init; } = "";
		public DateTimeOffset? ValidityStart  { get; init; }
		public DateTimeOffset? ValidityEnd    { get; init; }
	}

	static KontextConnectionPool NewPool(string dir) =>
		new($"Data Source={Path.Combine(dir, "engine.db")};access_mode=READ_WRITE", dir);

	// Dimension 4 matches the literal 4-dim vectors every test seeds.
	static KontextSchema NewSchema(KontextConnectionPool pool) => new(pool, new() { Dimension = 4 });

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
