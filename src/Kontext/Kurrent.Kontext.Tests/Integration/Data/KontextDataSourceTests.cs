// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.SemanticKernel.Connectors.DuckLance;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Behavioural tests for <see cref="KontextDataSource"/> against a REAL DuckDB + Lance engine:
/// the per-connection bootstrap (lance + ATTACH + USE redirection) over the in-memory engine,
/// and the disposed-source execute guard.
/// </summary>
[Category("Integration")]
public class KontextDataSourceTests {
	[Test]
	public async ValueTask attaches_the_lance_namespace_and_redirects_the_session_into_it() {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);

		// Act
		var info = await dataSources.ExecuteAsync(DuckDBEngineInfo.From);

		// Assert — every connection lands with the lance catalog attached AND current.
		await Assert.That(info.FindDatabase("ldb")).IsNotNull();
		await Assert.That(info.CurrentDatabase).IsEqualTo("ldb");
	}

	[Test]
	public async ValueTask refuses_to_execute_after_dispose() {
		// Arrange
		using var dir         = new TempDir();
		var       dataSources = MemorySeeding.NewDataSources(dir.Path);
		dataSources.Dispose();

		// Act
		ObjectDisposedException? exception = null;
		try {
			await dataSources.ExecuteAsync(_ => 0);
		} catch (ObjectDisposedException ex) {
			exception = ex;
		}

		// Assert
		await Assert.That(exception).IsNotNull();
	}

}
