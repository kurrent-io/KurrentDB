// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Pins the engine behaviors the entity verdict executor rests on, probed live against the lance
/// catalog — the writer's MERGE-only vocabulary covers neither of them:
/// 1. a matched-only MERGE may update the column it JOINED on, which is what refiles every mention
///    of a merged entity onto the survivor in ONE statement (no window where a mention sits under
///    both entities or neither)
/// 2. DELETE by equality removes exactly the matching rows, including the zero-row case, which is
///    what makes re-applying a verdict a no-op instead of an error
/// </summary>
[Category("Integration")]
[Timeout(30_000)]
public class EntityResolutionLanceProbeTests {
	[Test]
	public async ValueTask a_matched_merge_can_move_rows_onto_a_new_join_key(CancellationToken cancellationToken) {
		// Arrange — two mentions filed under the loser, one under the survivor.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		Exec(connection, "CREATE TABLE ldb.main.probe_refile (entity_id VARCHAR, memory_id VARCHAR)");
		Exec(connection, "INSERT INTO ldb.main.probe_refile VALUES ('loser', 'm1'), ('loser', 'm2'), ('survivor', 'm3')");

		// Act
		int refiled;

		using (var command = connection.CreateCommand()) {
			command.CommandText =
				"""
				MERGE INTO ldb.main.probe_refile AS t
				USING (SELECT $loser_id AS entity_id) AS s
				ON t.entity_id = s.entity_id
				WHEN MATCHED THEN UPDATE SET entity_id = $survivor_id
				""";
			command.Parameters.Add(new("loser_id", "loser"));
			command.Parameters.Add(new("survivor_id", "survivor"));
			refiled = command.ExecuteNonQuery();
		}

		// Assert — every row moved, none duplicated, none left behind.
		await Assert.That(refiled).IsEqualTo(2);
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.probe_refile WHERE entity_id = 'survivor'")).IsEqualTo(3L);
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.probe_refile WHERE entity_id = 'loser'")).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask delete_by_equality_removes_only_the_named_rows_and_tolerates_zero(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		Exec(connection, "CREATE TABLE ldb.main.probe_delete (source_entity_id VARCHAR, target_entity_id VARCHAR)");
		Exec(connection, "INSERT INTO ldb.main.probe_delete VALUES ('a', 'b'), ('a', 'c'), ('d', 'b')");

		// Act — one pair by its two-column key, then the same delete again.
		var first  = DeletePair(connection, "a", "b");
		var second = DeletePair(connection, "a", "b");

		// Assert — the pair went, its neighbors stayed, and the repeat is a no-op, not a failure.
		await Assert.That(first).IsEqualTo(1);
		await Assert.That(second).IsEqualTo(0);
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.probe_delete")).IsEqualTo(2L);

		static int DeletePair(DuckDBAdvancedConnection connection, string source, string target) {
			using var command = connection.CreateCommand();
			command.CommandText = "DELETE FROM ldb.main.probe_delete WHERE source_entity_id = $source AND target_entity_id = $target";
			command.Parameters.Add(new("source", source));
			command.Parameters.Add(new("target", target));
			return command.ExecuteNonQuery();
		}
	}

	static void Exec(DuckDBAdvancedConnection connection, string sql) {
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}

	static long Scalar(DuckDBAdvancedConnection connection, string sql) {
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return (long)command.ExecuteScalar()!;
	}
}
