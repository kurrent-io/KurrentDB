// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Behavioural tests for <see cref="KontextConnectionPool"/> against a REAL DuckDB + Lance engine:
/// the per-connection bootstrap (lance + ATTACH), the engine-side attach verification that guards
/// the silent stem-equals-alias data loss, and the frozen-pool execute guard.
/// </summary>
public class KontextConnectionPoolTests {
	[Test]
	public async ValueTask attaches_the_lance_namespace_and_verifies_it_engine_side() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = new KontextConnectionPool($"Data Source={Path.Combine(dir.Path, "engine.db")}", dir.Path);

		// Act — the first rented connection runs Initialize: LOAD lance, ATTACH, verify.
		var info = await pool.ExecuteAsync(DuckDBEngineInfo.From);

		// Assert — the alias is a separate catalog, distinct from the engine's own. (Its type
		// column reads 'duckdb' even for a Lance attach — validated live — hence the name check.)
		await Assert.That(info.FindDatabase("ldb")).IsNotNull();
		await Assert.That(info.CurrentDatabase).IsEqualTo("engine");
	}

	[Test]
	public async ValueTask rejects_an_engine_file_whose_stem_equals_the_alias() {
		// Arrange — the validated silent-data-loss shape: DuckDB names its own catalog after the
		// file stem ("ldb"), so ATTACH IF NOT EXISTS ... AS ldb silently no-ops and writes would
		// route away from Lance. The pool's engine-side verification must turn that into a loud
		// failure on the first operation.
		using var dir  = new TempDir();
		using var pool = new KontextConnectionPool($"Data Source={Path.Combine(dir.Path, "ldb.db")}", dir.Path);

		// Act
		InvalidOperationException? exception = null;
		try {
			await pool.ExecuteAsync(_ => 0);
		} catch (InvalidOperationException ex) {
			exception = ex;
		}

		// Assert
		await Assert.That(exception).IsNotNull();
		await Assert.That(exception!.Message).Contains("resolved to the engine's own catalog");
	}

	[Test]
	public async ValueTask refuses_to_execute_after_dispose() {
		// Arrange — Quack's Rent silently mints a fresh connection on a frozen pool, so the pool
		// itself must guard.
		using var dir  = new TempDir();
		var       pool = new KontextConnectionPool($"Data Source={Path.Combine(dir.Path, "engine.db")}", dir.Path);
		pool.Dispose();

		// Act
		ObjectDisposedException? exception = null;
		try {
			await pool.ExecuteAsync(_ => 0);
		} catch (ObjectDisposedException ex) {
			exception = ex;
		}

		// Assert
		await Assert.That(exception).IsNotNull();
	}

	/// <summary>A unique temp directory owned by one test; deleted on dispose.</summary>
	sealed class TempDir : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontext-pool-tests", Guid.NewGuid().ToString("N"));

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
