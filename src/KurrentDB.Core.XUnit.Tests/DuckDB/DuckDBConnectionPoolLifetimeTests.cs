// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.DuckDB;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.Chunks;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.DuckDB;

public class DuckDBConnectionPoolLifetimeTests : DirectoryPerTest<DuckDBConnectionPoolLifetimeTests> {
	private string DbDirectory => Fixture.Directory;

	// the directory DuckDB itself would pick for a database at <DbDirectory>/kurrent.ddb
	private string DefaultTempDirectory => Path.Combine(DbDirectory, "kurrent.ddb.tmp");

	private DuckDBConnectionPoolLifetime CreateSut(string sqlEngineTempDirectory = "") =>
		new(CreateDbConfig(sqlEngineTempDirectory), setups: [], log: null);

	private TFChunkDbConfig CreateDbConfig(string sqlEngineTempDirectory) =>
		new(DbDirectory,
			chunkSize: 10_000,
			maxChunksCacheSize: 0,
			databaseTag: new InMemoryCheckpoint(-1),
			writerCheckpoint: new InMemoryCheckpoint(0),
			chaserCheckpoint: new InMemoryCheckpoint(0),
			epochCheckpoint: new InMemoryCheckpoint(-1),
			proposalCheckpoint: new InMemoryCheckpoint(-1),
			truncateCheckpoint: new InMemoryCheckpoint(-1),
			replicationCheckpoint: new InMemoryCheckpoint(-1),
			indexCheckpoint: new InMemoryCheckpoint(-1),
			streamExistenceFilterCheckpoint: new InMemoryCheckpoint(-1)) {
			SqlEngineTempDirectory = sqlEngineTempDirectory,
		};

	private static async Task<string> CreateStaleFile(string directory, string name) {
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, name);
		await File.WriteAllTextAsync(path, "stale");
		return path;
	}

	[Fact]
	public async Task startup_removes_leftover_temp_objects_from_the_default_temp_directory() {
		// given
		var staleFile = await CreateStaleFile(DefaultTempDirectory, "duckdb_temp_storage_default-1.tmp");
		var staleDirectory = Path.Combine(DefaultTempDirectory, "duckdb_temp_block-1.tmp");
		await CreateStaleFile(staleDirectory, "duckdb_temp_block-1-1.tmp");

		using var sut = CreateSut();

		// when
		await sut.StartAsync(CancellationToken.None);

		// then
		Assert.False(File.Exists(staleFile));
		Assert.False(Directory.Exists(staleDirectory));
	}

	[Fact]
	public async Task startup_leaves_other_files_in_the_temp_directory_alone() {
		// given
		var otherFile = await CreateStaleFile(DefaultTempDirectory, "keep-me.txt");

		using var sut = CreateSut();

		// when
		await sut.StartAsync(CancellationToken.None);

		// then
		Assert.True(File.Exists(otherFile));
	}

	[Fact]
	public async Task startup_removes_leftover_temp_objects_from_the_configured_temp_directory() {
		// given
		var configuredTempDirectory = Path.Combine(DbDirectory, "spill");
		var staleFile = await CreateStaleFile(configuredTempDirectory, "duckdb_temp_storage_default-1.tmp");
		var staleFileElsewhere = await CreateStaleFile(DefaultTempDirectory, "duckdb_temp_storage_default-1.tmp");

		using var sut = CreateSut(configuredTempDirectory);

		// when
		await sut.StartAsync(CancellationToken.None);

		// then
		Assert.False(File.Exists(staleFile));
		Assert.True(File.Exists(staleFileElsewhere));
	}

	[Fact]
	public async Task startup_succeeds_when_the_temp_directory_does_not_exist() {
		// given nothing has spilled yet, so there may be no temp directory to clean up
		using var sut = CreateSut();

		// when, then startup still completes
		await sut.StartAsync(CancellationToken.None);
	}
}
