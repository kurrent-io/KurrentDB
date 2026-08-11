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
		// Arrange
		var options = Options(ratio: 0.15, floor: 1000);

		// Act + Assert
		await Assert.That(Decide(0, null, options, retrainDue: false)).IsEqualTo(KontextMaintenanceAction.None);
		await Assert.That(Decide(0, 0, options, retrainDue: true)).IsEqualTo(KontextMaintenanceAction.None);
	}

	[Test]
	public async ValueTask decide_ensures_when_the_index_is_missing_and_never_retrains_it() {
		// Arrange
		var options = Options(ratio: 0.15, floor: 1000);

		// Act + Assert
		await Assert.That(Decide(300, null, options, retrainDue: false)).IsEqualTo(KontextMaintenanceAction.EnsureVectorIndex);
		await Assert.That(Decide(300, null, options, retrainDue: true)).IsEqualTo(KontextMaintenanceAction.EnsureVectorIndex);
	}

	[Test]
	public async ValueTask decide_folds_only_when_floor_and_ratio_are_both_exceeded() {
		// Arrange
		var options = Options(ratio: 0.15, floor: 1000);

		// Act + Assert
		await Assert.That(Decide(100_000, 99_000, options, retrainDue: false)).IsEqualTo(KontextMaintenanceAction.None);

		// ratio 0.5 is well above 0.15 but unindexed 50 sits below the 1000-row floor: no fold.
		await Assert.That(Decide(100, 50, options, retrainDue: false)).IsEqualTo(KontextMaintenanceAction.None);

		// ratio 0.5 AND unindexed 5000: both hold, fold.
		await Assert.That(Decide(10_000, 5_000, options, retrainDue: false)).IsEqualTo(KontextMaintenanceAction.EnsureVectorIndex);
	}

	[Test]
	public async ValueTask decide_ratio_is_strict_and_floor_is_inclusive() {
		// Act + Assert
		await Assert.That(Decide(1000, 500, Options(ratio: 0.5, floor: 1), retrainDue: false)).IsEqualTo(KontextMaintenanceAction.None);

		// one row past the ratio threshold (501/1000 = 0.501) does trigger.
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

		// Act + Assert
		await Assert.That(Decide(10_000, 5_000, options, retrainDue: true)).IsEqualTo(KontextMaintenanceAction.RetrainVectorIndex);

		// A retrain fires even when the index is perfectly fresh — it is time-based, not backlog-based.
		await Assert.That(Decide(10_000, 10_000, options, retrainDue: true)).IsEqualTo(KontextMaintenanceAction.RetrainVectorIndex);
	}

	#endregion // Decide

	#region ->> Ticks <<-

	[Test]
	public async ValueTask tick_skips_quietly_before_the_schema_is_created() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = NewSchema(pool);

		using var scheduler = NewScheduler(schema);

		// Act
		await scheduler.TickNowAsync();

		// Assert
		await Assert.That(await schema.ExistsAsync()).IsFalse();
	}

	[Test]
	public async ValueTask tick_creates_the_vector_index_at_the_training_floor_then_folds_later_backlogs() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = NewSchema(pool);
		await schema.CreateAsync();
		SeedFillers(pool, 300, "r");

		using var scheduler = NewScheduler(schema);

		// Act
		await scheduler.TickNowAsync();

		// Assert
		await Assert.That(await schema.GetMaintenanceStateAsync()).IsEqualTo((300L, (long?)300L));

		// Act
		SeedFillers(pool, 50, "s");
		await scheduler.TickNowAsync();

		// Assert
		await Assert.That(await schema.GetMaintenanceStateAsync()).IsEqualTo((350L, (long?)350L));
	}

	[Test]
	public async ValueTask tick_leaves_the_index_uncreated_below_the_training_floor() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = NewSchema(pool);
		await schema.CreateAsync();
		SeedFillers(pool, 5, "r");

		using var scheduler = NewScheduler(schema);

		// Act
		await scheduler.TickNowAsync();

		// Assert
		await Assert.That(await schema.GetMaintenanceStateAsync()).IsEqualTo((5L, (long?)null));
	}

	[Test]
	public async ValueTask tick_retrains_on_cadence() {
		// Arrange
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

		// Act
		SeedFillers(pool, 50, "s");
		clock.Advance(TimeSpan.FromHours(25));
		await scheduler.TickNowAsync();

		// Assert
		var expectedState = (350L, (long?)350L);

		await Assert.That(await schema.GetMaintenanceStateAsync()).IsEqualTo(expectedState);

		// A tick right after the rebuild has nothing to do: the retrain clock was re-armed.
		await scheduler.TickNowAsync();
		await Assert.That(await schema.GetMaintenanceStateAsync()).IsEqualTo(expectedState);
	}

	[Test]
	public async ValueTask maintenance_statements_execute_directly_against_the_live_engine() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = NewSchema(pool);
		await schema.CreateAsync();
		SeedFillers(pool, 300, "r");
		await Assert.That(await schema.EnsureVectorIndexAsync()).IsTrue();

		// Act
		await schema.RetrainVectorIndexAsync();
		await schema.CompactAsync();
		await schema.VacuumAsync(TimeSpan.FromDays(14), retainVersions: 3);

		// Assert
		await Assert.That(await schema.GetMaintenanceStateAsync()).IsEqualTo((300L, (long?)300L));
	}

	[Test]
	public async ValueTask dispose_makes_later_ticks_safe_noops() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var scheduler = NewScheduler(NewSchema(pool));

		// Act
		scheduler.Dispose();
		await scheduler.TickNowAsync();
		scheduler.Dispose(); // double-dispose is equally safe

		// Assert
		await Assert.That(await NewSchema(pool).ExistsAsync()).IsFalse();
	}

	[Test]
	public async ValueTask real_timer_eventually_creates_the_index_and_folds_a_backlog() {
		// Arrange
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

		// Act + Assert
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
			   tags,
			   reasoning,
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
			        CAST([] AS VARCHAR[]),
			        '',
			        CAST([] AS VARCHAR[]),
			        CAST([] AS VARCHAR[]),
			        NULL,
			        NULL,
			        epoch_ms(TIMESTAMPTZ '2026-06-01 00:00:00+00'),
			        epoch_ms(TIMESTAMPTZ '2026-06-01 00:00:00+00'),
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
		new(dir);

	// Dimension 4 matches the literal 4-dim vectors the fillers seed.
	static KontextSchema NewSchema(KontextConnectionPool pool) => new(pool, new() { Dimension = 4 });


	#endregion // Test Infrastructure
}
