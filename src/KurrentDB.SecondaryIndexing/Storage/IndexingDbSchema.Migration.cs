// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Data;
using System.Diagnostics;
using Kurrent.Quack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.SecondaryIndexing.Storage;

partial class IndexingDbSchema {
	private const string VersionMetadataKey = "version";
	private const string MinimumVersion = "0";

	public static int PerformMigration(DuckDBAdvancedConnection connection,
		int desiredVersion = TargetVersion,
		ILoggerFactory? logger = null) {
		logger ??= NullLoggerFactory.Instance;

		int baseVersion;
		switch (CheckTables(connection)) {
			case (false, true):
				// infrastructure exists, but not idx_metadata, this is v0
				baseVersion = 0;
				break;
			case (false, false) when desiredVersion is TargetVersion:
				// fresh setup, create schema from scratch and leave
				CreateSchema(connection);
				return 0;
			case (true, true):
				// perform migration
				baseVersion = GetVersion(connection);
				break;
			default:
				// idx_metadata exists, but not infrastructure
				throw new DataException("Index database is broken");
		}

		if (baseVersion < desiredVersion) {
			PerformMigration(
				baseVersion,
				desiredVersion,
				connection,
				MigrationActions,
				logger.CreateLogger<IndexingDbSchema>());
		}

		return baseVersion;

		static (bool HasMetadataTable, bool HasIndexTables) CheckTables(DuckDBAdvancedConnection connection) {
			var hasMetadataTable = false;
			var hasDefaultIndexTable = false;

			foreach (var tableName in connection.GetTables()) {
				hasMetadataTable |= tableName.SequenceEqual("idx_metadata"u8);
				hasDefaultIndexTable |= tableName.SequenceEqual("idx_all"u8);
			}

			return (hasMetadataTable, hasDefaultIndexTable);
		}
	}

	private static void PerformMigration(
		int baseVersion,
		int targetVersion,
		DuckDBAdvancedConnection connection,
		IReadOnlyDictionary<int, Action<DuckDBAdvancedConnection>> actions,
		ILogger<IndexingDbSchema> log) {

		Debug.Assert(baseVersion < targetVersion);
		log.LogInformation("Start secondary index migration from {CurrentVersion} to {TargetVersion}", baseVersion, targetVersion);
		try {
			// Use transaction for each transition to avoid growth of DuckDB WAL
			for (baseVersion += 1; baseVersion <= targetVersion; baseVersion++) {
				log.LogInformation("Transitive migration to {TargetVersion}", baseVersion);
				DoUpgrade(connection, actions, baseVersion);
			}

			log.LogInformation("Secondary index migration completed successfully");
		} catch (Exception e) {
			log.LogCritical(e, "Failed secondary index migration");
		}

		static void DoUpgrade(
			DuckDBAdvancedConnection connection,
			IReadOnlyDictionary<int, Action<DuckDBAdvancedConnection>> actions,
			int targetVersion) {
			using var transaction = connection.BeginTransaction();
			if (actions.TryGetValue(targetVersion, out var action)) {
				action.Invoke(connection);
			}

			// update version
			connection.ExecuteNonQuery<int, UpdateVersionQuery>(targetVersion);
			transaction.CommitOnDispose();
		}
	}

	private static int GetVersion(DuckDBAdvancedConnection connection) {
		return GetVersion(LoadMetadata(connection));
	}

	private static int GetVersion(IReadOnlyDictionary<string, string?> metadata) {
		if (!int.TryParse(metadata.GetValueOrDefault(VersionMetadataKey, MinimumVersion), provider: null, out var version))
			version = 0;

		return version;
	}

	private static IReadOnlyDictionary<string, string?> LoadMetadata(DuckDBAdvancedConnection connection)
		=> connection
			.ExecuteQuery<KeyValuePair<string, string?>, MetadataQuery>()
			.ToDictionary();

	private static void SetTargetVersion(DuckDBAdvancedConnection connection)
		=> connection.ExecuteNonQuery<int, UpdateVersionQuery>(TargetVersion);
}

file readonly struct MetadataQuery : IQuery<KeyValuePair<string, string?>> {
	public static ReadOnlySpan<byte> CommandText => "SELECT * FROM idx_metadata;"u8;

	public static KeyValuePair<string, string?> Parse(ref DataChunk.Row row)
		=> new(row.ReadString(), row.TryReadString());
}

file readonly struct UpdateVersionQuery : IPreparedStatement<int> {
	public static ReadOnlySpan<byte> CommandText => "INSERT INTO idx_metadata (key, value) VALUES ('version', ?) ON CONFLICT(key) DO UPDATE SET value = EXCLUDED.value;"u8;

	public static StatementBindingResult Bind(in int version, PreparedStatement source) {
		Span<byte> buffer = stackalloc byte[64];
		version.TryFormat(buffer, out var bytesWritten);
		source.Bind(1, buffer.Slice(0, bytesWritten), BlobType.Utf8);

		return new(source, completed: true);
	}
}
