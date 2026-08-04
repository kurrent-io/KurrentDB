// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using TUnit.Assertions.Enums;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Retrieval;
using static Kurrent.Kontext.Contracts.MemoryImportance;
using static Kurrent.Kontext.Contracts.MemoryType;

namespace Kurrent.Kontext.Tests;

/// <summary>
/// Behavioural tests for <see cref="KontextMemory"/> against a REAL DuckDB + Lance engine, over the
/// projector-owned <see cref="KontextDataStore"/> read model. The write path is not built yet, so
/// each test seeds the memories table directly with SQL — exactly how the projector will write it —
/// and exercises the read-only surface the service exposes:
/// - the three write operations (retain, retract, reflect) throw NotImplementedException
/// - recall runs the embedding-free default pipeline (keyword BM25 → cognitive modulation → MMR),
///   lean by default, and never surfaces hidden memories
/// - reclaim is an exact-id passthrough that returns retracted memories too
/// - recollect lists by type/tag with a sort
///
/// Embeddings are seeded as literal 4-dim vectors so the table is well-formed; recall here has no
/// vector leg, so the vectors never decide a result.
/// </summary>
[Category("Integration")]
public class KontextMemoryTests {
	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask recall_finds_seeded_memories_by_keywords() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var expected = new MemoryRow(
			Id: "a1",
			Type: Observation,
			Content: "aardvark burrows deep underground",
			Importance: High,
			RetainedAt: Base.AddHours(1)
		);

		var store = await Seed(pool, [
			expected,
			new MemoryRow(
				Id: "a2",
				Type: Fact,
				Content: "penguins waddle across antarctic ice",
				Importance: Normal,
				RetainedAt: Base.AddHours(2)
			),
			new MemoryRow(
				Id: "a3",
				Type: Fact,
				Content: "giraffes browse the tallest acacia leaves",
				Importance: Low,
				RetainedAt: Base.AddHours(3)
			)
		]);

		var memory = NewMemory(store);

		// Act
		var response = await memory.RecallAsync(new Contracts.RecallRequest { Query = "aardvark" });

		// Assert
		await Assert.That(response.QueryId).IsNotEqualTo("");
		await Assert.That(response.Memories.Count).IsEqualTo(1);

		var hit = response.Memories[0];

		await Assert.That(hit.BodyCase).IsEqualTo(Contracts.RecallResponse.Types.RecalledMemory.BodyOneofCase.Lean);
		await Assert.That(hit.Full).IsNull();
		await Assert.That(hit.Score).IsGreaterThan(0);

		await Assert.That(hit.Lean.MemoryId).IsEqualTo(expected.Id);
		await Assert.That(hit.Lean.Content).IsEqualTo(expected.Content);
		await Assert.That(hit.Lean.MemoryType).IsEqualTo(expected.Type);
		await Assert.That(hit.Lean.Importance).IsEqualTo(expected.Importance);
		await Assert.That(hit.Lean.RetainedAt.ToDateTimeOffset()).IsEqualTo(expected.RetainedAt);
	}

	[Test]
	public async ValueTask recall_returns_full_memories_when_include_full_is_set() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var expected = new MemoryRow(
			Id: "b1",
			Type: Fact,
			Content: "flamingo stands gracefully on one leg",
			Importance: High,
			RetainedAt: Base.AddHours(1)
		) {
			Evidence      = SeedEvidence().ToByteArray(),
			ValidityStart = Base.AddHours(-24),
			ValidityEnd   = Base.AddHours(24),
		};

		var store  = await Seed(pool, expected);
		var memory = NewMemory(store);

		var request = new Contracts.RecallRequest { Query = "flamingo", IncludeFull = true };

		// Act
		var response = await memory.RecallAsync(request);

		// Assert
		await Assert.That(response.Memories.Count).IsEqualTo(1);

		var hit = response.Memories[0];

		await Assert.That(hit.BodyCase).IsEqualTo(Contracts.RecallResponse.Types.RecalledMemory.BodyOneofCase.Full);
		await Assert.That(hit.Lean).IsNull();
		await Assert.That(hit.Score).IsGreaterThan(0);
		await Assert.That(hit.Full.MemoryId).IsEqualTo(expected.Id);
		await Assert.That(hit.Full.Content).IsEqualTo(expected.Content);
		await Assert.That(hit.Full.Evidence).IsEqualTo(SeedEvidence());
		await Assert.That(hit.Full.Validity!.PerceivedStart.ToDateTimeOffset()).IsEqualTo(expected.ValidityStart!.Value);
	}

	[Test]
	public async ValueTask recall_never_surfaces_retracted_or_superseded_memories() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var living = new MemoryRow(
			Id: "c1",
			Type: Observation,
			Content: "wombat digs a cozy burrow",
			Importance: Normal,
			RetainedAt: Base.AddHours(1)
		);

		var store = await Seed(pool,
			living,
			new MemoryRow(
				Id: "c2",
				Type: Observation,
				Content: "wombat mistaken hidden note",
				Importance: Normal,
				RetainedAt: Base.AddHours(2)
			) {
				IsRetracted = true,
				RetractedAt = Base.AddHours(5),
			},
			new MemoryRow(
				Id: "c3",
				Type: Observation,
				Content: "wombat obsolete replaced entry",
				Importance: Normal,
				RetainedAt: Base.AddHours(3)
			) {
				IsSuperseded = true,
				SupersededAt = Base.AddHours(4),
				SupersededBy = living.Id,
			});
		var memory = NewMemory(store);

		var request         = new Contracts.RecallRequest { Query = "wombat" };
		var expectedVisible = new List<string> { living.Id };

		// Act
		var response = await memory.RecallAsync(request);

		// Assert
		var ids = response.Memories.Select(m => m.Lean.MemoryId).ToList();

		await Assert.That(ids).IsEquivalentTo(expectedVisible, CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask recall_filters_by_tags() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var tagged = new MemoryRow(
			Id: "d1",
			Type: Fact,
			Content: "salmon swim upstream every year",
			Importance: Normal,
			RetainedAt: Base.AddHours(1)
		) {
			Tags = ["project:rivers"],
		};

		var store = await Seed(pool,
			tagged,
			new MemoryRow(
				Id: "d2",
				Type: Fact,
				Content: "salmon spawn in shallow gravel",
				Importance: Normal,
				RetainedAt: Base.AddHours(2)
			));
		var memory = NewMemory(store);

		var request = new Contracts.RecallRequest {
			Query = "salmon",
			Tags = {
				new Contracts.Tag {
					Scope = "project",
					Value = "rivers"
				}
			}
		};
		var expectedTagged = new List<string> { tagged.Id };

		// Act
		var response = await memory.RecallAsync(request);

		// Assert
		var ids = response.Memories.Select(m => m.Lean.MemoryId).ToList();

		await Assert.That(ids).IsEquivalentTo(expectedTagged, CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask reclaim_returns_exact_ids_including_retracted() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var living = new MemoryRow(
			Id: "e1",
			Type: Observation,
			Content: "kangaroo hops across the plains",
			Importance: Normal,
			RetainedAt: Base.AddHours(1)
		);

		var retracted = new MemoryRow(
			Id: "e2",
			Type: Observation,
			Content: "kangaroo mistaken claim",
			Importance: Normal,
			RetainedAt: Base.AddHours(2)
		) {
			IsRetracted = true,
			RetractedAt = Base.AddHours(5),
		};

		var store  = await Seed(pool, living, retracted);
		var memory = NewMemory(store);

		var request = new Contracts.ReclaimRequest();
		request.Ids.AddRange([living.Id, retracted.Id, "no-such-memory"]);
		var expectedReturned = new List<string> { living.Id, retracted.Id };

		// Act
		var memories = await memory.ReclaimAsync(request).ToListAsync();

		// Assert
		var ids = memories.Select(m => m.MemoryId).Order().ToList();

		await Assert.That(ids).IsEquivalentTo(expectedReturned, CollectionOrdering.Matching);
		await Assert.That(memories.Single(m => m.MemoryId == retracted.Id).RetractedAt).IsNotNull();
	}

	[Test]
	public async ValueTask recollect_lists_by_type_and_sorts() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var highFact = new MemoryRow(
			Id: "f1",
			Type: Fact,
			Content: "fact about caching",
			Importance: High,
			RetainedAt: Base.AddHours(1)
		) { LastAccessedAt = Base.AddHours(10) };

		var criticalFact = new MemoryRow(
			Id: "f2",
			Type: Fact,
			Content: "fact about the checkpoint format",
			Importance: Critical,
			RetainedAt: Base.AddHours(2)
		) { LastAccessedAt = Base.AddHours(20) };

		var plan = new MemoryRow(
			Id: "f3",
			Type: Plan,
			Content: "plan to rewrite the projector",
			Importance: Normal,
			RetainedAt: Base.AddHours(3)
		) { LastAccessedAt = Base.AddHours(30) };

		var lowFact = new MemoryRow(
			Id: "f4",
			Type: Fact,
			Content: "fact about tags",
			Importance: Low,
			RetainedAt: Base.AddHours(4)
		) { LastAccessedAt = Base.AddHours(5) };

		var store  = await Seed(pool, highFact, criticalFact, plan, lowFact);
		var memory = NewMemory(store);

		var request = new Contracts.RecollectRequest {
			Sort      = Contracts.RecollectSort.Importance,
			Direction = Contracts.SortDirection.Descending,
		};
		request.Types_.Add(Fact);
		var expectedOrder = new List<string> { criticalFact.Id, highFact.Id, lowFact.Id };

		// Act
		var memories = await memory.RecollectAsync(request).ToListAsync();

		// Assert
		var ids = memories.Select(m => m.MemoryId).ToList();

		await Assert.That(ids).IsEquivalentTo(expectedOrder, CollectionOrdering.Matching);
	}

	#region ->> Test Infrastructure <<-

	/// <summary>A no-op append: the write path is not built, so nothing this service does emits events yet.</summary>
	static readonly AppendEvent NoOp = static (_, _) => Task.CompletedTask;

	/// <summary>The service over a keyword-only pipeline: these tests exercise recall's mapping, not the vector leg, so no embeddingGenerator is wired.</summary>
	static KontextMemory NewMemory(KontextDataStore store) =>
		new(store,
			KontextRetriever
				.New()
				.AddSearch(new KeywordSearch(store))
				.AddStage(CognitiveModulator.Create())
				.AddStage(MmrReorderer.Create())
				.Build(),
			NoOp);

	static Contracts.Evidence SeedEvidence() {
		var evidence = new Contracts.Evidence();
		evidence.Citations.Add(new Contracts.Evidence.Types.Citation { Memory = new() { Id = "cited-1" } });
		return evidence;
	}

	// Dimension 4 matches the literal 4-dim vectors every test seeds.
	static ValueTask<KontextDataStore> Seed(KontextConnectionPool pool, params MemoryRow[] rows) =>
		MemorySeeding.Seed(pool, dimension: 4, rows);

	static KontextConnectionPool NewPool(string dir) =>
		MemorySeeding.NewPool(dir);

	#endregion
}
