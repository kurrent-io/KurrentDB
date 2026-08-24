// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Kontext.Infrastructure.Data;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Pins whether a native STRUCT[] can hold the repeated citation column, which today is a VARCHAR[]
/// of JSON. STRUCT[] is the efficient shape — every struct field becomes its own Arrow child array,
/// so a read touches one buffer instead of parsing text — and evidence is a oneof, so structs whose
/// null fields differ across elements of one list is not an edge case but every memory citing more
/// than one kind of source.
///
/// Probed 2026-08-03 against lance-encoding 6.0.0 the read threw an internal decoder error, and the
/// column stayed VARCHAR[] for that reason. Re-probed 2026-08-23 it reads back correctly, so the
/// limitation has lifted and moving evidence to STRUCT[] is now a question of whether the migration
/// is worth it rather than whether the engine allows it. This test pins that it still works, so the
/// answer does not silently rot back.
/// </summary>
[Category("Integration")]
[Timeout(30_000)]
public class EvidenceStructBindingProbeTests {
	// A flat struct with a `kind` discriminator and sparse arms — the shape evidence would take.
	const string EvidenceType =
		"STRUCT(kind VARCHAR, memory_id VARCHAR, repo VARCHAR, commit VARCHAR, uri VARCHAR)[]";

	[Test]
	public async ValueTask struct_list_round_trips_when_fields_are_sparse(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		dataSources.Execute(connection =>
			Exec(connection, $"CREATE TABLE ldb.main.probe_evidence (memory_id VARCHAR, evidence {EvidenceType})"));

		// Act — two citations of different kinds, so `repo` is set on one element and null on the
		// other. That asymmetry is what the decoder used to mishandle.
		var write = Try(() => {
			dataSources.Execute(connection =>
				Exec(connection,
					"""
					INSERT INTO ldb.main.probe_evidence VALUES ('m1', [
					  {'kind': 'memory', 'memory_id': 'cited-1', 'repo': NULL, 'commit': NULL, 'uri': NULL},
					  {'kind': 'git', 'memory_id': NULL, 'repo': 'kurrent--kurrentdb', 'commit': 'c93c6ae82', 'uri': NULL}])
					"""));
		});

		var cited = dataSources.Execute(connection => {
			using var command = connection.CreateCommand();
			command.CommandText =
				"""
				SELECT evidence[1].memory_id, evidence[2].repo, evidence[1].repo
				FROM ldb.main.probe_evidence
				""";

			using var reader = command.ExecuteReader();
			reader.Read();

			return (First: reader.GetString(0), SecondRepo: reader.GetString(1), FirstRepo: reader.IsDBNull(2));
		});

		// Assert — both halves work, and the sparse arm reads as itself rather than as its neighbour:
		// the field set on one element only must not bleed across the list.
		await Assert.That(write).IsEqualTo("OK");
		await Assert.That(cited.First).IsEqualTo("cited-1");
		await Assert.That(cited.SecondRepo).IsEqualTo("kurrent--kurrentdb");
		await Assert.That(cited.FirstRepo).IsTrue();
	}

	static string Try(Action action) {
		try {
			action();
			return "OK";
		} catch (Exception ex) {
			return $"{ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}";
		}
	}

	static void Exec(DuckDBConnection connection, string sql) {
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}

	static KontextDataSource NewDataSources(string dir) => MemorySeeding.NewDataSources(dir);

	/// <summary>A unique temp directory owned by one test; deleted on dispose.</summary>
	sealed class TempDir : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontext-evidence-probe", Guid.NewGuid().ToString("N"));

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
