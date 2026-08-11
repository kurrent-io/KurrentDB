// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Behavioural tests for <see cref="KontextConnectionPool"/> against a REAL DuckDB + Lance engine:
/// the per-connection bootstrap (lance + ATTACH) over the in-memory engine catalog, the
/// engine-side attach verification, and the frozen-pool execute guard.
/// </summary>
[Category("Integration")]
public class KontextConnectionPoolTests {
	[Test]
	public async ValueTask attaches_the_lance_namespace_and_verifies_it_engine_side() {
		// Arrange
		using var dir  = new TempDir();
		using var pool = new KontextConnectionPool(dir.Path);

		// Act
		var info = await pool.ExecuteAsync(DuckDBEngineInfo.From);

		// Assert
		await Assert.That(info.FindDatabase("ldb")).IsNotNull();
		await Assert.That(info.CurrentDatabase).IsEqualTo("memory");
	}

	[Test]
	public async ValueTask refuses_to_execute_after_dispose() {
		// Arrange
		using var dir  = new TempDir();
		var       pool = new KontextConnectionPool(dir.Path);
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
