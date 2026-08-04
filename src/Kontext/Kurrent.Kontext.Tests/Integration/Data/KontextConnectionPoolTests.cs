// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Behavioural tests for <see cref="KontextConnectionPool"/> against a REAL DuckDB + Lance engine:
/// the per-connection bootstrap (lance + ATTACH), the engine-side attach verification that guards
/// the silent stem-equals-alias data loss, and the frozen-pool execute guard.
/// </summary>
[Category("Integration")]
public class KontextConnectionPoolTests {
	[Test]
	public async ValueTask attaches_the_lance_namespace_and_verifies_it_engine_side() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = new KontextConnectionPool($"Data Source={Path.Combine(dir.Path, "engine.db")}", dir.Path);

		// Act
		var info = await pool.ExecuteAsync(DuckDBEngineInfo.From);

		// Assert
		await Assert.That(info.FindDatabase("ldb")).IsNotNull();
		await Assert.That(info.CurrentDatabase).IsEqualTo("engine");
	}

	[Test]
	public async ValueTask rejects_an_engine_file_whose_stem_equals_the_alias() {
		// Arrange
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
		// Arrange
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

}
