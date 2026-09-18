// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Storage;
using Microsoft.Extensions.Logging;

namespace KurrentDB.SecondaryIndexing.Tests.Migration;

partial class MigrationTests {
	// A migration must not need temp space proportional to the size of the index. V1A did: its
	// DEFAULT ''::BLOB drove DuckDB to rewrite the ADD COLUMN into an UPDATE of every row, needing
	// ~20 bytes of transaction local memory per row where V1B needs ~2. On a large index that exhausts
	// max_temp_directory_size, and the rollback fails for the same reason and takes the node down.
	//
	// Rather than build an index large enough to exhaust a realistic budget, this shrinks the budget:
	// transient space is capped at a few bytes per row. That makes the assertion a statement about per
	// row cost, which is the part that scales to production, and it runs in well under a second.
	private const int TempBudgetRows = 400_000;

	private const int TempBudgetBytesPerRow = 5;

	// Chosen so that the migrations we ship need no temp space at all here, which keeps the test
	// deterministic: at 4MB they spill ~1.1MiB against a ~1.9MiB budget, and DuckDB's spill volume
	// varies enough run to run that it intermittently crossed. At 8MB they spill nothing, while V1A
	// still needs ~15MiB and so fails by a wide margin. Measured 10/10 either way.
	//
	// Note the budget therefore bounds memory and temp together rather than temp alone. That is the
	// useful invariant: a migration must not need transient space proportional to the index.
	private const string TempBudgetMemoryLimit = "8MB";

	[Fact]
	public async Task MigratingFromV0FitsInTempBudget() =>
		await AssertMigrationsFitInTempBudget(StartFromV0, startingVersion: 0, TempBudgetBytesPerRow);

	// From V1A only V2 remains, which is catalog only, so it should need next to nothing. V1A itself
	// cannot fit any budget - that is the bug - so it runs while the database is being built, before the
	// budget is imposed.
	[Fact]
	public async Task MigratingFromV1AFitsInTempBudget() =>
		await AssertMigrationsFitInTempBudget(StartFromV1A, startingVersion: 1, bytesPerRow: 1);

	// The control for the two above: replay V1A under the same budget they pass under, and confirm it
	// blows it. Without this they could be passing because the budget was never applied, or because the
	// migrations never ran. Asserts why it failed, not just that it did, so an unrelated breakage cannot
	// masquerade as the budget working.
	[Fact]
	public async Task MigratingWithTheOriginalV1ExceedsTempBudget() {
		Dictionary<int, Action<DuckDBAdvancedConnection>> justV1A = new() {
			[1] = IndexingDbSchema.UpgradeToV1A,
		};

		var logs = await MigrateUnderTempBudget(
			StartFromV0,
			startingVersion: 0,
			desiredVersion: 1,
			TempBudgetBytesPerRow,
			justV1A);

		var failure = logs.Entries.FirstOrDefault(x => x.Level >= LogLevel.Error);

		Assert.NotNull(failure.Exception);
		Assert.Contains("max_temp_directory_size", failure.Exception.ToString());
	}

	private async Task AssertMigrationsFitInTempBudget(
		Action<DuckDBAdvancedConnection> migrateFromV0ToStartingVersion,
		int startingVersion,
		int bytesPerRow) {

		var logs = await MigrateUnderTempBudget(
			migrateFromV0ToStartingVersion,
			startingVersion,
			IndexingDbSchema.TargetVersion,
			bytesPerRow,
			IndexingDbSchema.MigrationActions,
			AssertSeededRowsSurvived);

		var failure = logs.Entries.FirstOrDefault(x => x.Level >= LogLevel.Error);

		Assert.True(failure.Exception is null,
			$"migration failed: {failure.Message}{Environment.NewLine}{failure.Exception}");
	}

	// Returns whatever the migration logged. PerformMigration swallows failures into LogCritical, so
	// without capturing them a failure surfaces only as a confusing error from a later assertion.
	private async Task<CapturingLoggerFactory> MigrateUnderTempBudget(
		Action<DuckDBAdvancedConnection> migrateFromV0ToStartingVersion,
		int startingVersion,
		int desiredVersion,
		int bytesPerRow,
		IReadOnlyDictionary<int, Action<DuckDBAdvancedConnection>> actions,
		Action<DuckDBAdvancedConnection>? inspect = null) {

		var dbPath = Fixture.GetFilePathFor($"{GetType().Name}.budget.v{startingVersion}to{desiredVersion}.db");

		// Build a populated database, then close it. Seeding leaves the buffer pool warm, which would
		// absorb the spilling the budget exists to constrain, and a migrating node is reopening a
		// database rather than continuing with one it has just written.
		await using (DuckDBAdvancedConnection seeding = new() { ConnectionString = $"Data Source={dbPath};" }) {
			seeding.Open();
			SetupV0Schema(seeding);
			SeedV0Rows(seeding, TempBudgetRows);
			migrateFromV0ToStartingVersion(seeding);
			seeding.ExecuteAdHocNonQuery("CHECKPOINT;"u8, multipleStatements: false);
		}

		await using DuckDBAdvancedConnection connection = new() { ConnectionString = $"Data Source={dbPath};" };
		connection.Open();

		// threads is pinned because how much gets spilled varies with how much runs in parallel
		connection.ExecuteAdHocNonQuery(
			$"""
			 SET memory_limit='{TempBudgetMemoryLimit}';
			 SET threads=2;
			 PRAGMA max_temp_directory_size='{TempBudgetRows * bytesPerRow}B';
			 """,
			multipleStatements: true);

		CapturingLoggerFactory logs = new();

		IndexingDbSchema.PerformMigration(
			startingVersion,
			desiredVersion,
			connection,
			actions,
			logs.CreateLogger<IndexingDbSchema>());

		// Only worth inspecting when the migration got through - exceeding the budget invalidates the
		// connection, so every subsequent query fails too.
		if (!logs.Entries.Exists(x => x.Level >= LogLevel.Error))
			inspect?.Invoke(connection);

		return logs;
	}

	private static void AssertSeededRowsSurvived(DuckDBAdvancedConnection connection) {
		var stats = connection.ExecuteQuery<RecordIdStats, RecordIdStatsQuery>().ToList();
		string[] tables = ["idx_all", "idx_user__myindex"];

		foreach (var table in tables) {
			var rows = stats.Single(x => x.Table == table);

			Assert.Equal(TempBudgetRows, rows.Rows);
			Assert.Equal(0, rows.Nulls);
			Assert.Equal(0, rows.NonEmptyBytes);
		}
	}

	private sealed class CapturingLoggerFactory : ILoggerFactory {
		public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

		public ILogger CreateLogger(string categoryName) => new Recorder(Entries);

		public void AddProvider(ILoggerProvider provider) {
		}

		public void Dispose() {
		}

		private sealed class Recorder(List<(LogLevel, string, Exception?)> entries) : ILogger {
			public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

			public bool IsEnabled(LogLevel logLevel) => true;

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
				Func<TState, Exception?, string> formatter)
				=> entries.Add((logLevel, formatter(state, exception), exception));
		}
	}
}
