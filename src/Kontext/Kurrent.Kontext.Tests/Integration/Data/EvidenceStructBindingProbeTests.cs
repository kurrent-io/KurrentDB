// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Kontext.Infrastructure.Data;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Pins why the repeated citation column is a VARCHAR[] of JSON rather than a native STRUCT[],
/// probed live 2026-08-03 against lance-encoding 6.0.0.
///
/// STRUCT[] is the efficient shape on paper — every struct field becomes its own Arrow child array,
/// so a read touches one buffer instead of parsing text. It is unusable here: the column CREATEs and
/// WRITEs, but reading a list whose structs differ in which fields are null throws an internal
/// decoder error. Evidence is a oneof, so sparse fields varying across elements in one list is not
/// an edge case — it is every memory that cites more than one kind of source.
///
/// This test asserts the LIMITATION. It turns red the day the decoder is fixed, which is exactly
/// when moving evidence to STRUCT[] becomes worth revisiting.
/// </summary>
[Category("Integration")]
[Timeout(30_000)]
public class EvidenceStructBindingProbeTests {
	// A flat struct with a `kind` discriminator and sparse arms — the shape evidence would take.
	const string EvidenceType =
		"STRUCT(kind VARCHAR, memory_id VARCHAR, repo VARCHAR, commit VARCHAR, uri VARCHAR)[]";

	[Test]
	public async ValueTask struct_list_writes_but_cannot_be_read_back_when_fields_are_sparse(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		dataSources.Execute(connection =>
			Exec(connection, $"CREATE TABLE ldb.main.probe_evidence (memory_id VARCHAR, evidence {EvidenceType})"));

		// Act — two citations of different kinds, so `repo` is set on one element and null on the
		// other. That asymmetry is what the decoder mishandles.
		var write = Try(() => {
			dataSources.Execute(connection =>
				Exec(connection,
					"""
					INSERT INTO ldb.main.probe_evidence VALUES ('m1', [
					  {'kind': 'memory', 'memory_id': 'cited-1', 'repo': NULL, 'commit': NULL, 'uri': NULL},
					  {'kind': 'git', 'memory_id': NULL, 'repo': 'kurrent--kurrentdb', 'commit': 'c93c6ae82', 'uri': NULL}])
					"""));
		});

		var read = Try(() => {
			dataSources.Execute(connection => {
				using var command = connection.CreateCommand();
				command.CommandText = "SELECT evidence[1].memory_id FROM ldb.main.probe_evidence";
				command.ExecuteScalar();
			});
		});

		// Assert — the write half is fine, so this is a decode limitation and not bad SQL.
		await Assert.That(write).IsEqualTo("OK");

		// The engine's own wording, pinned: an internal error naming the sparse child array.
		await Assert.That(read).Contains("Incorrect array length for StructArray");
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
