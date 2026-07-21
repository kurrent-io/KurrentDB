// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Microsoft.Extensions.Time.Testing;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Two halves, matching the scheduler's own split:
/// - Decide tests are pure — no engine anywhere, just the per-tick decision table
/// - tick tests run against a REAL DuckDB + Lance engine through the same pool + schema pair the
///   host wires, driving ticks deterministically with TickNowAsync (plus one real-timer smoke test)
/// </summary>
[Category("Integration")]
public class KontextMaintenanceSchedulerTests {
	static readonly DateTimeOffset Now = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

	#region ->> Decide <<-

	[Test]
	public async ValueTask decide_does_nothing_on_an_empty_table() {
		// Arrange — an empty table can neither train an index nor usefully optimize one.
		var options = Options(ratio: 0.15, floor: 1000);

		// Act + Assert — with or without an index, and even with a retrain overdue.
		await Assert.That(Decide(0, null, options, retrainDue: false)).IsEqualTo(KontextMaintenanceAction.None);
		await Assert.That(Decide(0, 0, options, retrainDue: true)).IsEqualTo(KontextMaintenanceAction.None);
	}

	[Test]
	public async ValueTask decide_ensures_when_the_index_is_missing_and_never_retrains_it() {
		// Arrange
		var options = Options(ratio: 0.15, floor: 1000);

		// Act + Assert — a missing index is always an ensure attempt (the schema owns the training
		// floor), even when the retrain clock says a rebuild is overdue: there is nothing to rebuild.
		await Assert.That(Decide(300, null, options, retrainDue: false)).IsEqualTo(KontextMaintenanceAction.EnsureVectorIndex);
		await Assert.That(Decide(300, null, options, retrainDue: true)).IsEqualTo(KontextMaintenanceAction.EnsureVectorIndex);
	}

	[Test]
	public async ValueTask decide_folds_only_when_floor_and_ratio_are_both_exceeded() {
		// Arrange
		var options = Options(ratio: 0.15, floor: 1000);

		// Act + Assert — unindexed 1000 meets the floor but ratio 0.01 is below 0.15: no fold.
		await Assert.That(Decide(100_000, 99_000, options, retrainDue: false)).IsEqualTo(KontextMaintenanceAction.None);

		// ratio 0.5 is well above 0.15 but unindexed 50 sits below the 1000-row floor: no fold.
		await Assert.That(Decide(100, 50, options, retrainDue: false)).IsEqualTo(KontextMaintenanceAction.None);

		// ratio 0.5 AND unindexed 5000: both hold, fold.
		await Assert.That(Decide(10_000, 5_000, options, retrainDue: false)).IsEqualTo(KontextMaintenanceAction.EnsureVectorIndex);
	}

	[Test]
	public async ValueTask decide_ratio_is_strict_and_floor_is_inclusive() {
		// Act + Assert — ratio exactly at the threshold (500/1000 = 0.5) does NOT trigger…
		await Assert.That(Decide(1000, 500, Options(ratio: 0.5, floor: 1), retrainDue: false)).IsEqualTo(KontextMaintenanceAction.None);

		// …but one row past it (501/1000 = 0.501) does.
		await Assert.That(Decide(1000, 499, Options(ratio: 0.5, floor: 1), retrainDue: false)).IsEqualTo(KontextMaintenanceAction.EnsureVectorIndex);

		// unindexed exactly at the floor (100) DOES trigger…
		await Assert.That(Decide(200, 100, Options(ratio: 0.1, floor: 100), retrainDue: false)).IsEqualTo(KontextMaintenanceAction.EnsureVectorIndex);

		// …but one below it (99) does not.
		await Assert.That(Decide(200, 101, Options(ratio: 0.1, floor: 100), retrainDue: false)).IsEqualTo(KontextMaintenanceAction.None);
	}

	[Test]
	public async ValueTask decide_retrains_on_cadence_with_precedence_over_a_fold() {
		// Arrange
		var options = Options(ratio: 0.15, floor: 1000);

		// Act + Assert — the same snapshot that would fold above, but a retrain is due: rebuild wins.
		await Assert.That(Decide(10_000, 5_000, options, retrainDue: true)).IsEqualTo(KontextMaintenanceAction.RetrainVectorIndex);

		// A retrain fires even when the index is perfectly fresh — it is time-based, not backlog-based.
		await Assert.That(Decide(10_000, 10_000, options, retrainDue: true)).IsEqualTo(KontextMaintenanceAction.RetrainVectorIndex);
	}

	#endregion // Decide

	#region ->> Ticks <<-

	[Test]
	public async ValueTask tick_skips_quietly_before_the_schema_is_created() {
		// Arrange — CreateAsync is deliberately never called: no table, no dataset.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = NewSchema(pool);

		using var scheduler = NewScheduler(schema);

		// Act — must not throw.
		await scheduler.TickNowAsync();

		// Assert — the tick created nothing: the table still does not exist.
		await Assert.That(await schema.ExistsAsync()).IsFalse();
	}

	[Test]
	public async ValueTask tick_creates_the_vector_index_at_the_training_floor_then_folds_later_backlogs() {
		// Arrange — 300 rows crosses the ~256-row training floor; eager thresholds make any
		// backlog fold-worthy.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = NewSchema(pool);
		await schema.CreateAsync();
		SeedFillers(pool, 300, "r");

		using var scheduler = NewScheduler(schema);

		// Act — the first tick catch-up-creates the missing index (and compacts + vacuums).
		await scheduler.TickNowAsync();

		// Assert — the fresh index covers every row.
		await Assert.That(await schema.GetMaintenanceStateAsync()).IsEqualTo((300L, (long?)300L));

		// Act — 50 more rows land past the index; the next tick folds them in.
		SeedFillers(pool, 50, "s");
		await scheduler.TickNowAsync();

		// Assert
		await Assert.That(await schema.GetMaintenanceStateAsync()).IsEqualTo((350L, (long?)350L));
	}

	[Test]
	public async ValueTask tick_leaves_the_index_uncreated_below_the_training_floor() {
		// Arrange — 5 rows: far below the engine's ~256-row training floor.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = NewSchema(pool);
		await schema.CreateAsync();
		SeedFillers(pool, 5, "r");

		using var scheduler = NewScheduler(schema);

		// Act — must not throw: the ensure attempt reports the floor honestly, hygiene still runs.
		await scheduler.TickNowAsync();

		// Assert — rows counted, index honestly absent.
		await Assert.That(await schema.GetMaintenanceStateAsync()).IsEqualTo((5L, (long?)null));
	}

	[Test]
	public async ValueTask tick_retrains_on_cadence() {
		// Arrange — default thresholds so a 50-row backlog is NOT fold-worthy: only the retrain
		// clock can explain the index catching up. The tick interval far exceeds the advanced
		// time so the fake clock's Advance never fires timer ticks underneath the deterministic
		// TickNowAsync calls (FakeTimeProvider caps timer due times at ~49 days — uint32 ms).
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = NewSchema(pool);
		await schema.CreateAsync();
		SeedFillers(pool, 300, "r");

		var clock = new FakeTimeProvider(Now);

		var options = new KontextMaintenanceOptions {
			TickInterval    = TimeSpan.FromDays(30),
			RetrainInterval = TimeSpan.FromHours(24),
		};

		using var scheduler = new KontextMaintenanceScheduler(schema, options, clock);

		// The creation tick also starts the retrain clock (a creation IS a full train).
		await scheduler.TickNowAsync();
		await Assert.That(await schema.GetMaintenanceStateAsync()).IsEqualTo((300L, (long?)300L));

		// Act — a 50-row backlog appears (below the 1000-row floor: no fold), and 25 hours pass.
		SeedFillers(pool, 50, "s");
		clock.Advance(TimeSpan.FromHours(25));
		await scheduler.TickNowAsync();

		// Assert — the time-based full rebuild retrained over ALL current rows.
		await Assert.That(await schema.GetMaintenanceStateAsync()).IsEqualTo((350L, (long?)350L));

		// A tick right after the rebuild has nothing to do: the retrain clock was re-armed.
		await scheduler.TickNowAsync();
		await Assert.That(await schema.GetMaintenanceStateAsync()).IsEqualTo((350L, (long?)350L));
	}

	[Test]
	public async ValueTask maintenance_statements_execute_directly_against_the_live_engine() {
		// Arrange — the tick body swallows failures by design, so a broken maintenance statement
		// would never fail a tick test; each one is exercised HERE, where it throws loudly.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = NewSchema(pool);
		await schema.CreateAsync();
		SeedFillers(pool, 300, "r");
		await Assert.That(await schema.EnsureVectorIndexAsync()).IsTrue();

		// Act — the full maintenance pass, statement by statement.
		await schema.RetrainVectorIndexAsync();
		await schema.CompactAsync();
		await schema.VacuumAsync(TimeSpan.FromDays(14), retainVersions: 3);

		// Assert — the dataset survived with the index intact and fully current.
		await Assert.That(await schema.GetMaintenanceStateAsync()).IsEqualTo((300L, (long?)300L));
	}

	[Test]
	public async ValueTask dispose_makes_later_ticks_safe_noops() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var scheduler = NewScheduler(NewSchema(pool));

		// Act — dispose, then tick: chosen over throwing ObjectDisposedException.
		scheduler.Dispose();
		await scheduler.TickNowAsync();
		scheduler.Dispose(); // double-dispose is equally safe

		// Assert — nothing was touched: the table was never created.
		await Assert.That(await NewSchema(pool).ExistsAsync()).IsFalse();
	}

	[Test]
	public async ValueTask real_timer_eventually_creates_the_index_and_folds_a_backlog() {
		// Arrange — a fast (200ms) real timer with eager thresholds: background ticks must both
		// create the index and, later, fold a backlog without any TickNowAsync call.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = NewSchema(pool);
		await schema.CreateAsync();
		SeedFillers(pool, 300, "r");

		var options = new KontextMaintenanceOptions {
			TickInterval            = TimeSpan.FromMilliseconds(200),
			UnindexedRowFloor       = 1,
			UnindexedRatioThreshold = 0.01,
		};

		using var scheduler = new KontextMaintenanceScheduler(schema, options);

		// Act + Assert — phase 1: a background tick creates the vector index at 300 rows.
		await Assert.That(await PollAsync(schema, expected: (300L, 300L), TimeSpan.FromSeconds(10))).IsTrue();

		// Phase 2: a later background tick folds a fresh 50-row backlog in.
		SeedFillers(pool, 50, "s");
		await Assert.That(await PollAsync(schema, expected: (350L, 350L), TimeSpan.FromSeconds(10))).IsTrue();
	}

	#endregion // Ticks

	#region ->> Test Infrastructure <<-

	/// <summary>Runs the pure decision, deriving the retrain clock from <paramref name="retrainDue"/>: overdue by a day, or fresh as of now.</summary>
	static KontextMaintenanceAction Decide(long totalRows, long? vectorIndexRows, KontextMaintenanceOptions options, bool retrainDue) {
		var lastRetrain = retrainDue ? Now - options.RetrainInterval - TimeSpan.FromDays(1) : Now;

		return KontextMaintenanceScheduler.Decide(
			totalRows, vectorIndexRows, options,
			lastRetrain, Now);
	}

	static KontextMaintenanceOptions Options(double ratio, int floor) =>
		new() {
			UnindexedRatioThreshold = ratio,
			UnindexedRowFloor       = floor,
			RetrainInterval         = TimeSpan.FromHours(24),
		};

	/// <summary>An eager scheduler for deterministic ticks: any backlog folds, and the real timer never fires mid-test.</summary>
	static KontextMaintenanceScheduler NewScheduler(KontextSchema schema) =>
		new(
			schema, new() {
				TickInterval            = TimeSpan.FromHours(1),
				UnindexedRowFloor       = 1,
				UnindexedRatioThreshold = 0.01,
			});

	/// <summary>Polls the maintenance state until it matches <paramref name="expected"/> or <paramref name="timeout"/> elapses.</summary>
	static async Task<bool> PollAsync(KontextSchema schema, (long, long?) expected, TimeSpan timeout) {
		var deadline = DateTimeOffset.UtcNow + timeout;

		while (DateTimeOffset.UtcNow < deadline) {
			if (await TryReadStateAsync(schema) == expected)
				return true;

			await Task.Delay(TimeSpan.FromMilliseconds(100));
		}

		return await TryReadStateAsync(schema) == expected;
	}

	static async Task<(long, long?)?> TryReadStateAsync(KontextSchema schema) {
		try {
			return await schema.GetMaintenanceStateAsync();
		} catch (Exception) {
			// A concurrent maintenance tick (compaction/vacuum) can momentarily disturb a cached
			// dataset handle; treat a transient read failure as "not ready yet" and poll again.
			return null;
		}
	}

	/// <summary>
	/// Bulk-seeds <paramref name="count"/> filler rows ENGINE-SIDE: one statement, no parameters,
	/// deterministic — the same proven shape the store tests seed with. The id prefix keeps
	/// successive seed batches distinct.
	/// </summary>
	static void SeedFillers(KontextConnectionPool pool, int count, string idPrefix) {
		var sql =
			$"""
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
			 SELECT '{idPrefix}-' || i,
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
			 FROM range({count}) AS t(i)
			 """;

		using (pool.Rent(out var connection)) {
			using var command = connection.CreateCommand();
			command.CommandText = sql;
			command.ExecuteNonQuery();
		}
	}

	static KontextConnectionPool NewPool(string dir) =>
		new($"Data Source={Path.Combine(dir, "engine.db")};access_mode=READ_WRITE", dir);

	// Dimension 4 matches the literal 4-dim vectors the fillers seed.
	static KontextSchema NewSchema(KontextConnectionPool pool) => new(pool, new() { Dimension = 4 });

	/// <summary>A unique temp directory owned by one test; deleted on dispose.</summary>
	sealed class TempDir : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontext-maintenance-scheduler-tests", Guid.NewGuid().ToString("N"));

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
