// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.DuckDB;
using KurrentDB.Core.TransactionLog.Chunks;

namespace KurrentDB.Embedded.Tests;

[Timeout(180_000)]
public class EmbeddedResourceProfileTests {
	[Test]
	public async Task resource_defaults_match_the_build_profile() {
		var directory = TestPaths.NewDataDirectory();
		try {
			await using var db = new EmbeddedKurrentDB(TestPaths.Options(directory));
			var options = db.ClusterVNodeOptions.Database;
#if KURRENT_EMBEDDED_MINIMAL
			options.ChunkSize.ShouldBe(TFConsts.ChunkSize / 2);
			options.CachedChunks.ShouldBe(1);
			options.UseIndexBloomFilters.ShouldBeFalse();
			options.StreamExistenceFilterSize.ShouldBe(0);
#else
			options.ChunkSize.ShouldBe(TFConsts.ChunkSize);
			options.CachedChunks.ShouldBe(-1);
			options.UseIndexBloomFilters.ShouldBeTrue();
#endif
		} finally {
			TestPaths.Delete(directory);
		}
	}

	[Test]
	public async Task explicit_settings_override_profile_defaults() {
		var directory = TestPaths.NewDataDirectory();
		try {
			await using var db = new EmbeddedKurrentDB(TestPaths.Options(directory) with {
				DatabaseOptions = new Dictionary<string, string?> {
					["KurrentDB:ChunkSize"] = "67108864",
					["KurrentDB:CachedChunks"] = "2",
					["KurrentDB:UseIndexBloomFilters"] = "true",
					["KurrentDB:StreamExistenceFilterSize"] = "1048576",
				}
			});
			db.ClusterVNodeOptions.Database.ChunkSize.ShouldBe(64 * 1024 * 1024);
			db.ClusterVNodeOptions.Database.CachedChunks.ShouldBe(2);
			db.ClusterVNodeOptions.Database.UseIndexBloomFilters.ShouldBeTrue();
			db.ClusterVNodeOptions.Database.StreamExistenceFilterSize.ShouldBe(1024 * 1024);
		} finally {
			TestPaths.Delete(directory);
		}
	}

	[Test]
	public async Task rejects_a_negative_duckdb_memory_limit() {
		var directory = TestPaths.NewDataDirectory();
		try {
			await using var db = new EmbeddedKurrentDB(TestPaths.Options(directory) with {
				DatabaseOptions = new Dictionary<string, string?> {
					["KurrentDB:SqlEngineMemoryLimit"] = "-1"
				}
			});
			await Should.ThrowAsync<ArgumentOutOfRangeException>(() => db.StartAsync());
		} finally {
			TestPaths.Delete(directory);
		}
	}

	[Test]
	public async Task explicit_duckdb_memory_limit_reaches_the_connection_pool() {
		var directory = TestPaths.NewDataDirectory();
		try {
			await using var db = new EmbeddedKurrentDB(TestPaths.Options(directory) with {
				DatabaseOptions = new Dictionary<string, string?> {
					["KurrentDB:SqlEngineMemoryLimit"] = "268435456"
				}
			});
			await db.StartAsync();
			var config = db.Services.GetRequiredService<TFChunkDbConfig>();
			config.SqlEngineMemoryLimit.ShouldBe(256L * 1024 * 1024);
#if KURRENT_EMBEDDED_MINIMAL
			config.ChunkSize.ShouldBe(TFConsts.ChunkSize / 2);
			config.MaxChunksCacheSize.ShouldBe((long)config.ChunkSize + ChunkHeader.Size + ChunkFooter.Size);
#endif
			var pool = db.Services.GetRequiredService<DuckDBConnectionPoolLifetime>();
			using (pool.Shared.Rent(out var connection)) {
				using var command = connection.CreateCommand();
				command.CommandText = "SELECT current_setting('memory_limit')";
				command.ExecuteScalar().ShouldBe("256.0 MiB");
			}
		} finally {
			TestPaths.Delete(directory);
		}
	}
}
