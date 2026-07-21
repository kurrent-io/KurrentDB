// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Google.Protobuf;
using TUnit.Assertions.Enums;
using Kurrent.Kontext.Data;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Behavioural tests for <see cref="KontextDataStoreV2"/> against a REAL DuckDB + Lance engine.
/// The store is read-only, so each test seeds the memories table directly with SQL — exactly how
/// the projector will write it — through the same borrowed pool the store reads from. No vector
/// store and no embedding model anywhere: embeddings are seeded as literal 4-dim vectors, and
/// searches pass the query embedding in.
/// </summary>
public class KontextDataStoreV2Tests {
	static readonly DateTimeOffset Base = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	static Contracts.Evidence SeedEvidence() {
		var evidence = new Contracts.Evidence();
		evidence.Citations.Add(new Contracts.Evidence.Types.Citation { Memory = new() { Id = "cited-1" } });
		return evidence;
	}

	[Test]
	public async ValueTask get_by_id_round_trips_every_field() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act
		var stored = await store.GetAsync("m1");

		// Assert
		await Assert.That(stored).IsNotNull();
		await Assert.That(stored!.MemoryType).IsEqualTo((Contracts.MemoryType)1);
		await Assert.That(stored.Content).IsEqualTo("memory one");
		await Assert.That(stored.Importance).IsEqualTo((Contracts.MemoryImportance)3);
		await Assert.That(stored.Sentiment).IsEqualTo((Contracts.MemorySentiment)1);
		await Assert.That(stored.Urgency).IsEqualTo((Contracts.MemoryUrgency)2);
		await Assert.That(stored.Tags.Count).IsEqualTo(2);
		await Assert.That(stored.Tags[0].Scope).IsEqualTo("work");
		await Assert.That(stored.Tags[0].Value).IsEqualTo("alpha");
		await Assert.That(stored.Evidence).IsEqualTo(SeedEvidence());
		await Assert.That(stored.Validity!.PerceivedStart.ToDateTimeOffset()).IsEqualTo(Base.AddHours(-24));
		await Assert.That(stored.Validity.PerceivedEnd.ToDateTimeOffset()).IsEqualTo(Base.AddHours(24));
		await Assert.That(stored.RetainedAt.ToDateTimeOffset()).IsEqualTo(Base.AddHours(1));
		await Assert.That(stored.LastAccessedAt.ToDateTimeOffset()).IsEqualTo(Base.AddHours(30));
		await Assert.That(stored.RetractedAt).IsNull();
		await Assert.That(stored.SupersededAt).IsNull();
		await Assert.That(stored.SupersededBy).IsEqualTo("");
	}

	[Test]
	public async ValueTask get_by_id_returns_null_for_missing_and_never_hides_lifecycle() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act + Assert — missing is null; retracted and superseded still come back.
		await Assert.That(await store.GetAsync("no-such-memory")).IsNull();
		await Assert.That((await store.GetAsync("m3"))!.RetractedAt).IsNotNull();

		var superseded = await store.GetAsync("m4");
		await Assert.That(superseded!.SupersededAt).IsNotNull();
		await Assert.That(superseded.SupersededBy).IsEqualTo("m2");
	}

	[Test]
	public async ValueTask get_by_ids_returns_present_and_skips_missing() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act
		var memories = await store.GetAsync(["m1", "m3", "no-such-memory"]).ToListAsync();

		// Assert
		await Assert.That(memories.Select(m => m.MemoryId).Order().ToList()).IsEquivalentTo(["m1", "m3"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask list_hides_retracted_keeps_superseded_and_sorts_by_retained_descending() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act — no filters, default sort (retained at), newest first.
		var memories = await store.ListAsync(
				[], [], Contracts.RecollectSort.RetainedAt,
				Contracts.SortDirection.Descending, 10)
			.ToListAsync();

		// Assert — m3 is retracted and hidden; m4 is superseded and stays.
		await Assert.That(memories.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m2", "m4", "m1", "m5"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask list_filters_by_any_of_types_and_all_tags() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act + Assert — any-of types: 1 or 3 (retracted m3 stays hidden even though its type matches).
		var byTypes = await store.ListAsync(
				[], [(Contracts.MemoryType)1, (Contracts.MemoryType)3],
				Contracts.RecollectSort.RetainedAt, Contracts.SortDirection.Descending, 10)
			.ToListAsync();

		await Assert.That(byTypes.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m4", "m1"], CollectionOrdering.Matching);

		// ALL tags must be present: work:alpha AND team:blue.
		var byTags = await store.ListAsync(
				[Tag("work", "alpha"), Tag("team", "blue")], [],
				Contracts.RecollectSort.RetainedAt, Contracts.SortDirection.Descending, 10)
			.ToListAsync();

		await Assert.That(byTags.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m4", "m1"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask list_sorts_by_importance_and_last_accessed_and_limits() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act + Assert — importance ascending.
		var byImportance = await store.ListAsync(
				[], [], Contracts.RecollectSort.Importance,
				Contracts.SortDirection.Ascending, 10)
			.ToListAsync();

		await Assert.That(byImportance.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m2", "m4", "m1", "m5"], CollectionOrdering.Matching);

		// Last accessed descending, limited to the two freshest.
		var byAccess = await store.ListAsync(
				[], [], Contracts.RecollectSort.LastAccessedAt,
				Contracts.SortDirection.Descending, 2)
			.ToListAsync();

		await Assert.That(byAccess.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m5", "m1"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask lineage_returns_the_whole_family_from_any_member_in_chronological_order() {
		// Arrange — the family: L4 (living head) replaced L3, which had consolidated L1 and L2.
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		SeedLineage(pool);

		var expected = new List<string> { "L1", "L2", "L3", "L4" };

		// Act — start the walk at a leaf, in the middle, and at the head.
		var fromLeaf   = await store.GetLineageAsync("L1").ToListAsync();
		var fromMiddle = await store.GetLineageAsync("L3").ToListAsync();
		var fromHead   = await store.GetLineageAsync("L4").ToListAsync();

		// Assert — same family, same chronological order, no matter where the walk starts —
		// and the unrelated m1–m5 rows never leak in.
		await Assert.That(fromLeaf.Select(m => m.MemoryId).ToList()).IsEquivalentTo(expected, CollectionOrdering.Matching);
		await Assert.That(fromMiddle.Select(m => m.MemoryId).ToList()).IsEquivalentTo(expected, CollectionOrdering.Matching);
		await Assert.That(fromHead.Select(m => m.MemoryId).ToList()).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask lineage_of_an_unsuperseded_memory_is_itself_and_a_missing_id_streams_nothing() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act + Assert — m1 has no supersession edges at all: a family of one.
		var alone = await store.GetLineageAsync("m1").ToListAsync();

		await Assert.That(alone.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m1"], CollectionOrdering.Matching);

		// A missing id streams nothing — the same silence as GetAsync.
		await Assert.That(await store.GetLineageAsync("no-such-memory").ToListAsync()).IsEmpty();
	}

	[Test]
	public async ValueTask lineage_keeps_the_up_chain_when_the_successor_has_a_one_sided_edge() {
		// Arrange — the seed's m4 says "superseded_by m2", but m2's supersedes list is empty:
		// a one-sided edge. The walk runs on the superseded_by column alone (in both
		// directions), so the incomplete supersedes ARRAY cannot lose m4 from its own family.
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act
		var family = await store.GetLineageAsync("m4").ToListAsync();

		// Assert — m4 (retained earlier), then m2.
		await Assert.That(family.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m4", "m2"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask list_by_importance_breaks_ties_by_last_access_in_the_same_direction() {
		// Arrange — m1, m6, m7 all share importance 3; inside that tie group only last access
		// differs: m6 (Base+50h) > m1 (Base+30h) > m7 (Base+10h).
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		SeedTieRows(pool);

		// Act — the same list both ways.
		var best = await store.ListAsync(
				[], [], Contracts.RecollectSort.Importance,
				Contracts.SortDirection.Descending, 10)
			.ToListAsync();

		var evict = await store.ListAsync(
				[], [], Contracts.RecollectSort.Importance,
				Contracts.SortDirection.Ascending, 10)
			.ToListAsync();

		// Assert — descending: most important first, and inside the importance-3 tie the most
		// recently USED first. Ascending flips the tie-break too: least important, longest
		// untouched first — the eviction sweep.
		await Assert.That(best.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m5", "m6", "m1", "m7", "m4", "m2"], CollectionOrdering.Matching);
		await Assert.That(evict.Select(m => m.MemoryId).ToList()).IsEquivalentTo(["m2", "m4", "m7", "m1", "m6", "m5"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask list_settles_exact_ties_deterministically_by_memory_id() {
		// Arrange — m6 and m7 share the exact same retained_at instant, so the sort key alone
		// cannot order them.
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		SeedTieRows(pool);

		// Act — the identical call twice.
		var first = await store.ListAsync(
				[], [], Contracts.RecollectSort.RetainedAt,
				Contracts.SortDirection.Descending, 10)
			.ToListAsync();

		var second = await store.ListAsync(
				[], [], Contracts.RecollectSort.RetainedAt,
				Contracts.SortDirection.Descending, 10)
			.ToListAsync();

		// Assert — memory_id settles the m6/m7 tie the same way every time: without it the
		// engine may order rows inside a tie group differently between two identical calls.
		var expected = new List<string> { "m6", "m7", "m2", "m4", "m1", "m5" };

		await Assert.That(first.Select(m => m.MemoryId).ToList()).IsEquivalentTo(expected, CollectionOrdering.Matching);
		await Assert.That(second.Select(m => m.MemoryId).ToList()).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask search_ranks_by_vector_similarity_when_alpha_is_pure_vector() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act — the query vector sits on m1's axis; alpha 1 ignores keywords entirely.
		var hits = await store.SearchAsync(
				"anything", [1f, 0f, 0f, 0f], [],
				new() { Alpha = 1, Limit = 2 })
			.ToListAsync();

		// Assert — m1 exactly on the axis, m5 right next to it; scores descend, and the per-leg
		// diagnostics ride along: an exact match has vector distance 0.
		await Assert.That(hits.Select(h => h.Memory.MemoryId).ToList()).IsEquivalentTo(["m1", "m5"], CollectionOrdering.Matching);
		await Assert.That(hits[0].HybridScore!.Value).IsGreaterThanOrEqualTo(hits[1].HybridScore!.Value);
		await Assert.That(hits[0].VectorDistance!.Value).IsEqualTo(0);
		await Assert.That(hits[1].VectorDistance!.Value).IsGreaterThan(0);
	}

	[Test]
	public async ValueTask search_ranks_by_keywords_when_alpha_is_pure_text() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act — the vector deliberately points at m1, but alpha 0 means only keywords count.
		var hits = await store.SearchAsync(
				"projector checkpoint format", [1f, 0f, 0f, 0f], [],
				new() { Alpha = 0, Limit = 1 })
			.ToListAsync();

		// Assert — the keyword leg wins: m2 despite the m1-pointing vector, and its BM25 score
		// is a real positive relevance.
		await Assert.That(hits.Count).IsEqualTo(1);
		await Assert.That(hits[0].Memory.MemoryId).IsEqualTo("m2");
		await Assert.That(hits[0].KeywordScore!.Value).IsGreaterThan(0);
	}

	[Test]
	public async ValueTask search_never_surfaces_retracted_or_superseded_memories() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act — each query aims straight at the hidden row: its unique word AND its vector axis.
		var zebra  = await store.SearchAsync("zebra", [0f, 0f, 1f, 0f], []).ToListAsync();
		var quokka = await store.SearchAsync("quokka", [0f, 0f, 0f, 1f], []).ToListAsync();

		// Assert — m3 is retracted, m4 superseded; recall never sees either.
		await Assert.That(zebra.Select(h => h.Memory.MemoryId).ToList()).DoesNotContain("m3");
		await Assert.That(quokka.Select(h => h.Memory.MemoryId).ToList()).DoesNotContain("m4");
	}

	[Test]
	public async ValueTask search_with_tags_oversamples_past_a_tiny_k() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act — K = 1 alone would keep only the vector-closest candidate (m2, which lacks the
		// tag); the store must raise the pool to the table size so tagged rows still surface.
		var hits = await store.SearchAsync(
				"anything", [0f, 1f, 0f, 0f], [Tag("team", "blue")],
				new() { K = 1, Limit = 10 })
			.ToListAsync();

		// Assert — exactly the non-hidden team:blue rows (m4 has the tag but is superseded).
		await Assert.That(hits.Select(h => h.Memory.MemoryId).Order().ToList()).IsEquivalentTo(["m1", "m5"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask vector_search_ranks_nearest_first_with_exact_match_at_distance_zero() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act — pure vector mode: the query vector sits exactly on m1's axis, m5 right next to it.
		var hits = await store.SearchAsync([1f, 0f, 0f, 0f], [], new() { Limit = 2 }).ToListAsync();

		// Assert — nearest first (_distance ascending): the exact match at distance 0, and the
		// scores this mode never produces stay null.
		await Assert.That(hits.Select(h => h.Memory.MemoryId).ToList()).IsEquivalentTo(["m1", "m5"], CollectionOrdering.Matching);
		await Assert.That(hits[0].VectorDistance!.Value).IsEqualTo(0);
		await Assert.That(hits[1].VectorDistance!.Value).IsGreaterThan(0);
		await Assert.That(hits[0].HybridScore).IsNull();
		await Assert.That(hits[0].KeywordScore).IsNull();
	}

	[Test]
	public async ValueTask vector_search_never_surfaces_retracted_or_superseded_memories() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act — each query vector aims straight at a hidden row's axis.
		var zebra  = await store.SearchAsync([0f, 0f, 1f, 0f], []).ToListAsync();
		var quokka = await store.SearchAsync([0f, 0f, 0f, 1f], []).ToListAsync();

		// Assert — m3 is retracted, m4 superseded; recall never sees either.
		await Assert.That(zebra.Select(h => h.Memory.MemoryId).ToList()).DoesNotContain("m3");
		await Assert.That(quokka.Select(h => h.Memory.MemoryId).ToList()).DoesNotContain("m4");
	}

	[Test]
	public async ValueTask vector_search_with_tags_oversamples_past_a_tiny_k() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act — K = 1 alone would keep only the vector-closest candidate (m2, which lacks the
		// tag); the store must raise the pool to the table size so tagged rows still surface.
		var hits = await store.SearchAsync([0f, 1f, 0f, 0f], [Tag("team", "blue")], new() { K = 1, Limit = 10 }).ToListAsync();

		// Assert — exactly the non-hidden team:blue rows (m4 has the tag but is superseded).
		await Assert.That(hits.Select(h => h.Memory.MemoryId).Order().ToList()).IsEquivalentTo(["m1", "m5"], CollectionOrdering.Matching);
	}

	[Test]
	public async ValueTask fulltext_search_ranks_by_keywords_with_a_real_bm25_score() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act — pure keyword mode: no vector anywhere near this call. The options type is spelled
		// out because a bare new() would make the call ambiguous with the hybrid overload
		// (target-typed new converts to anything during overload resolution).
		var hits = await store.SearchAsync("projector checkpoint format", [], new FullTextSearchOptions { Limit = 1 }).ToListAsync();

		// Assert — the BM25 winner with a real positive relevance, and the scores this mode
		// never produces stay null.
		await Assert.That(hits.Count).IsEqualTo(1);
		await Assert.That(hits[0].Memory.MemoryId).IsEqualTo("m2");
		await Assert.That(hits[0].KeywordScore!.Value).IsGreaterThan(0);
		await Assert.That(hits[0].VectorDistance).IsNull();
		await Assert.That(hits[0].HybridScore).IsNull();
	}

	[Test]
	public async ValueTask fulltext_search_never_surfaces_retracted_or_superseded_memories() {
		// Arrange
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		// Act — each query aims straight at a hidden row's unique word.
		var zebra  = await store.SearchAsync("zebra", []).ToListAsync();
		var quokka = await store.SearchAsync("quokka", []).ToListAsync();

		// Assert — m3 is retracted, m4 superseded; recall never sees either.
		await Assert.That(zebra.Select(h => h.Memory.MemoryId).ToList()).DoesNotContain("m3");
		await Assert.That(quokka.Select(h => h.Memory.MemoryId).ToList()).DoesNotContain("m4");
	}

	[Test]
	public async ValueTask search_finds_the_exact_match_through_a_trained_ivf_hnsw_pq_index() {
		// Arrange — 5 seeded rows + 300 fillers crosses the ~256-row index training floor.
		using var dir   = new TempDir();
		using var pool  = NewPool(dir.Path);
		var       store = await Seed(pool);

		await SeedFillersAndCreateVectorIndex(pool);

		// The index must really exist — otherwise this test silently re-tests the brute-force path.
		var indexes = await pool.ExecuteAsync(connection => {
			using var command = connection.CreateCommand();
			command.CommandText = $"SHOW INDEXES ON '{DatasetPath(pool)}'";

			var       names  = new List<string>();
			using var reader = command.ExecuteReader();

			while (reader.Read())
				names.Add(reader.GetString(0));

			return names;
		});

		await Assert.That(indexes).Contains("vec_idx");

		// Act — pure vector search for m1's EXACT vector, now answered through the ANN index.
		var hits = await store.SearchAsync(
				"anything", [1f, 0f, 0f, 0f], [],
				new() { Alpha = 1, Limit = 1 })
			.ToListAsync();

		// Assert — top-1 only, on purpose: refine_factor re-ranks with exact distances, so a
		// distance-0 match cannot lose. ANN flakiness lives in near-tie membership at the MARGINS
		// of the top-k; asserting the exact-match winner keeps this test out of that class.
		await Assert.That(hits[0].Memory.MemoryId).IsEqualTo("m1");

		// The optional knobs must be accepted by the engine: nprobs bounds the IVF probe count,
		// use_index = false forces the exact brute-force path — same winner either way.
		var exact = await store.SearchAsync(
				"anything", [1f, 0f, 0f, 0f], [],
				new() { Alpha = 1, Limit = 1, Nprobs = 1, UseIndex = false })
			.ToListAsync();

		await Assert.That(exact[0].Memory.MemoryId).IsEqualTo("m1");

		// The pure vector overload answers through the same trained index: exact-match top-1.
		var vector = await store.SearchAsync([1f, 0f, 0f, 0f], [], new() { Limit = 1 }).ToListAsync();

		await Assert.That(vector[0].Memory.MemoryId).IsEqualTo("m1");

		// And its optional knobs must be accepted by the engine too — same winner on the exact path.
		var vectorExact = await store.SearchAsync(
				[1f, 0f, 0f, 0f], [],
				new() { Limit = 1, Nprobs = 1, UseIndex = false })
			.ToListAsync();

		await Assert.That(vectorExact[0].Memory.MemoryId).IsEqualTo("m1");
	}

	[Test]
	public async ValueTask schema_create_is_idempotent_and_creates_the_eager_indexes() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = NewSchema(pool);

		// Act — twice on purpose: bootstrap runs on every host start and must be re-runnable.
		await schema.CreateAsync();
		await schema.CreateAsync();

		// Assert — every eager index exists; the vector index does not (empty table sits far
		// below the training floor).
		var indexes = await schema.ListIndexesAsync();

		await Assert.That(indexes).Contains("content_fts");
		await Assert.That(indexes).Contains("memory_id_idx");
		await Assert.That(indexes).Contains("superseded_by_idx");
		await Assert.That(indexes).Contains("tags_idx");
		await Assert.That(indexes).DoesNotContain("vec_idx");
	}

	[Test]
	public async ValueTask schema_vector_index_waits_for_the_training_floor() {
		// Arrange — five seeded rows: far below the ~256-row training floor.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		await Seed(pool);

		var schema = NewSchema(pool);

		// Act
		var ready = await schema.EnsureVectorIndexAsync();

		// Assert — honestly reported as not created; search stays correct meanwhile because
		// below the floor vector search is an exact brute-force scan.
		await Assert.That(ready).IsFalse();
		await Assert.That(await schema.ListIndexesAsync()).DoesNotContain("vec_idx");
	}

	// ------------------------------------------------------------------------------------------------
	// Test infrastructure.
	// ------------------------------------------------------------------------------------------------

	static Contracts.Tag Tag(string scope, string value) => new() { Scope = scope, Value = value };

	/// <summary>Creates the schema through <see cref="KontextSchema"/> and seeds the five fixed rows, then hands back a store over the same pool.</summary>
	static async ValueTask<KontextDataStoreV2> Seed(KontextConnectionPool pool) {
		// The schema component owns CREATE TABLE and every eager index (including the FTS
		// INVERTED index the keyword tests need) — seeding only inserts rows.
		await NewSchema(pool).CreateAsync();

		// One multi-row INSERT: five fixed tuples, nineteen parameters each, bound row by row
		// in AddRow's column order. Kept apart from the schema DDL because parameters don't
		// prepare across a multi-statement batch — the one justified exception to
		// single-command batching.
		const string insertRowsSql =
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
			  (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?),
			  (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?),
			  (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?),
			  (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?),
			  (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
			""";

		using (pool.Rent(out var connection)) {
			using (var insert = connection.CreateCommand()) {
				insert.CommandText = insertRowsSql;

				// Each row sits on its own vector axis, so vector-search tests are deterministic;
				// contents carry distinct words for the keyword-search tests.

				// m5: earliest retained, freshest access, highest importance; near m1's axis.
				AddRow(insert, "m5", 2, "quick brown dog runs", 5, 0, 0, ["team:blue"], [], [],
					null, null, Base, Base.AddHours(40), false, null, false, null, "", [0.9f, 0.1f, 0f, 0f]);

				// m1: the full-field row — evidence with a citation, validity window, two tags.
				AddRow(insert, "m1", 1, "memory one", 3, 1, 2, ["work:alpha", "team:blue"], SeedEvidence().ToByteArray(), [],
					Base.AddHours(-24), Base.AddHours(24), Base.AddHours(1), Base.AddHours(30), false, null, false, null, "", [1f, 0f, 0f, 0f]);

				// m4: superseded by m2 — visible in listings, marked in reads, hidden from search.
				AddRow(insert, "m4", 3, "legacy quokka wisdom", 2, 0, 0, ["work:alpha", "team:blue"], [], [],
					null, null, Base.AddHours(2), Base.AddHours(10), false, null, true, Base.AddHours(3), "m2", [0f, 0f, 0f, 1f]);

				// m2: latest retained, lowest importance.
				AddRow(insert, "m2", 2, "projector checkpoint format switched", 1, 0, 0, ["work:alpha"], [], [],
					null, null, Base.AddHours(3), Base.AddHours(20), false, null, false, null, "", [0f, 1f, 0f, 0f]);

				// m3: retracted — hidden from listings and search, still readable by id.
				AddRow(insert, "m3", 1, "secret zebra fact", 4, 0, 0, [], [], [],
					null, null, Base.AddHours(4), Base.AddHours(4), true, Base.AddHours(5), false, null, "", [0f, 0f, 1f, 0f]);

				insert.ExecuteNonQuery();
			}
		}

		return new(pool);
	}

	/// <summary>Crosses the vector-index training floor with 300 filler rows, then creates the IVF_HNSW_PQ index through <see cref="KontextSchema"/>.</summary>
	static async ValueTask SeedFillersAndCreateVectorIndex(KontextConnectionPool pool) {
		// Fillers are generated ENGINE-SIDE: one statement, no parameters, deterministic. Their
		// vectors spread over the (z, w) circle, far from every axis the search tests query.
		const string fillersSql =
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
			SELECT 'filler-' || i,
			       1,
			       'filler content ' || i,
			       0,
			       0,
			       0,
			       CAST([] AS VARCHAR[]),
			       ''::BLOB,
			       CAST([] AS VARCHAR[]),
			       NULL,
			       NULL,
			       TIMESTAMPTZ '2026-06-01 00:00:00+00',
			       TIMESTAMPTZ '2026-06-01 00:00:00+00',
			       false,
			       NULL,
			       false,
			       NULL,
			       '',
			       CAST([0.1, 0.1, cos(i), sin(i)] AS FLOAT[4])
			FROM range(300) AS t(i)
			""";

		using (pool.Rent(out var connection)) {
			using var fillers = connection.CreateCommand();
			fillers.CommandText = fillersSql;
			fillers.ExecuteNonQuery();
		}

		// The schema component owns the vector index; the fillers above just crossed the
		// training floor, so the first call must create it. The second call exercises the
		// other half of the lifecycle against the real engine: index exists => append-optimize.
		var schema = NewSchema(pool);

		await Assert.That(await schema.EnsureVectorIndexAsync()).IsTrue();
		await Assert.That(await schema.EnsureVectorIndexAsync()).IsTrue();
	}

	/// <summary>One complete supersession family: L4 (living head) replaced L3, which had consolidated L1 and L2.</summary>
	static void SeedLineage(KontextConnectionPool pool) {
		// Literal, engine-side values — no parameters, fully deterministic:
		// - the tree: L4 -> L3 -> {L1, L2}; only L4 is still living
		// - edges are SYMMETRIC (each superseded_by has its matching supersedes entry),
		//   the way the projector writes them
		// - retained_at ascends L1 < L2 < L3 < L4, so chronological order is the tree bottom-up
		// - embeddings sit away from every axis the search tests query
		const string sql =
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
			  ('L1', 1, 'lineage first belief', 1, 0, 0, CAST([] AS VARCHAR[]), ''::BLOB, CAST([] AS VARCHAR[]),
			   NULL, NULL, TIMESTAMPTZ '2026-07-01 15:00:00+00', TIMESTAMPTZ '2026-07-01 15:00:00+00',
			   false, NULL, true, TIMESTAMPTZ '2026-07-01 17:00:00+00', 'L3', CAST([0.0, 0.0, 0.6, 0.8] AS FLOAT[4])),
			  ('L2', 1, 'lineage second belief', 1, 0, 0, CAST([] AS VARCHAR[]), ''::BLOB, CAST([] AS VARCHAR[]),
			   NULL, NULL, TIMESTAMPTZ '2026-07-01 16:00:00+00', TIMESTAMPTZ '2026-07-01 16:00:00+00',
			   false, NULL, true, TIMESTAMPTZ '2026-07-01 17:00:00+00', 'L3', CAST([0.0, 0.0, 0.8, 0.6] AS FLOAT[4])),
			  ('L3', 1, 'lineage consolidated belief', 2, 0, 0, CAST([] AS VARCHAR[]), ''::BLOB, CAST(['L1', 'L2'] AS VARCHAR[]),
			   NULL, NULL, TIMESTAMPTZ '2026-07-01 17:00:00+00', TIMESTAMPTZ '2026-07-01 17:00:00+00',
			   false, NULL, true, TIMESTAMPTZ '2026-07-01 18:00:00+00', 'L4', CAST([0.0, 0.0, 0.7, 0.7] AS FLOAT[4])),
			  ('L4', 1, 'lineage current belief', 3, 0, 0, CAST([] AS VARCHAR[]), ''::BLOB, CAST(['L3'] AS VARCHAR[]),
			   NULL, NULL, TIMESTAMPTZ '2026-07-01 18:00:00+00', TIMESTAMPTZ '2026-07-01 18:00:00+00',
			   false, NULL, false, NULL, '', CAST([0.0, 0.0, 0.5, 0.9] AS FLOAT[4]))
			""";

		using (pool.Rent(out var connection)) {
			using var command = connection.CreateCommand();
			command.CommandText = sql;
			command.ExecuteNonQuery();
		}
	}

	/// <summary>Two extra rows that TIE on purpose — the raw material for the ordering-guarantee tests.</summary>
	static void SeedTieRows(KontextConnectionPool pool) {
		// Literal, engine-side values — no parameters, fully deterministic:
		// - importance 3 matches m1, so {m7, m1, m6} form one importance tie group
		// - last access ranks inside that group: m6 (Base+50h) > m1 (Base+30h) > m7 (Base+10h)
		// - m6 and m7 share retained_at (Base+6h) EXACTLY, so only memory_id can order them
		// - embeddings sit away from every axis the search tests query
		const string sql =
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
			  ('m6', 1, 'tie row six', 3, 0, 0, CAST([] AS VARCHAR[]), ''::BLOB, CAST([] AS VARCHAR[]),
			   NULL, NULL, TIMESTAMPTZ '2026-07-01 16:00:00+00', TIMESTAMPTZ '2026-07-03 12:00:00+00',
			   false, NULL, false, NULL, '', CAST([0.5, 0.5, 0.0, 0.0] AS FLOAT[4])),
			  ('m7', 1, 'tie row seven', 3, 0, 0, CAST([] AS VARCHAR[]), ''::BLOB, CAST([] AS VARCHAR[]),
			   NULL, NULL, TIMESTAMPTZ '2026-07-01 16:00:00+00', TIMESTAMPTZ '2026-07-01 20:00:00+00',
			   false, NULL, false, NULL, '', CAST([0.4, 0.6, 0.0, 0.0] AS FLOAT[4]))
			""";

		using (pool.Rent(out var connection)) {
			using var command = connection.CreateCommand();
			command.CommandText = sql;
			command.ExecuteNonQuery();
		}
	}

	static string DatasetPath(KontextConnectionPool pool) => System.IO.Path.Combine(pool.StoragePath, "memories.lance");

	// Binds one VALUES tuple, in InsertRowsSql's column order; null binds as NULL.
	static void AddRow(
		DuckDBCommand command,
		string memoryId, int memoryType, string content, int importance, int sentiment, int urgency,
		List<string> tags, byte[] evidence, List<string> supersedes,
		DateTimeOffset? validityStart, DateTimeOffset? validityEnd,
		DateTimeOffset retainedAt, DateTimeOffset lastAccessedAt,
		bool isRetracted, DateTimeOffset? retractedAt,
		bool isSuperseded, DateTimeOffset? supersededAt, string supersededBy,
		float[] embedding
	) {
		object?[] values = [
			memoryId, memoryType, content, importance, sentiment, urgency, tags, evidence,
			supersedes, validityStart, validityEnd, retainedAt, lastAccessedAt,
			isRetracted, retractedAt, isSuperseded, supersededAt, supersededBy, embedding,
		];

		foreach (var value in values)
			command.Parameters.Add(new DuckDBParameter(value ?? DBNull.Value));
	}

	static KontextConnectionPool NewPool(string dir) =>
		new($"Data Source={Path.Combine(dir, "engine.db")};access_mode=READ_WRITE", dir);

	// Dimension 4 matches the literal 4-dim vectors every test seeds.
	static KontextSchema NewSchema(KontextConnectionPool pool) => new(pool, new() { Dimension = 4 });

	/// <summary>A unique temp directory owned by one test; deleted on dispose.</summary>
	sealed class TempDir : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontext-datastore-v2-tests", Guid.NewGuid().ToString("N"));

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
}
