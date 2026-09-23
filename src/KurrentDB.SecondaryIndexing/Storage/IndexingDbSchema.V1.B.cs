// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using DuckDB.NET.Data;
using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Indexes.User;

namespace KurrentDB.SecondaryIndexing.Storage;

partial class IndexingDbSchema {
	// Replaces the original V1 migration (which is now renamed to V1A).
	//
	// Does the same thing but uses a record_id DEFAULT of '' instead of ''::BLOB
	//
	// DuckDB identifies '' as a constant and materializes approximately 10x less temporary data during
	// the migration than with the cast (which prevents it from being detected as a constant, see transform_alter_table.cpp).
	// The new version is less likely to exhaust max_temp_directory_size and fail.
	//
	// Databases already upgraded by the earlier version of this migration have the same row data
	// (every pre-upgrade row holds the empty BLOB), but their catalog records the default as CAST('' AS "BLOB").
	// Both forms evaluate to the same value. The only difference is the catalog.
	private static void UpgradeToV1B(DuckDBAdvancedConnection connection) {
		// Add record_id column and rename columns
		connection.ExecuteAdHocNonQuery(
			"""
			CREATE TABLE idx_metadata(key varchar primary key not null, value varchar);
			ALTER TABLE idx_all ADD COLUMN record_id BLOB DEFAULT '';
			ALTER TABLE idx_all RENAME COLUMN event_number TO stream_revision;
			ALTER TABLE idx_all RENAME COLUMN created TO created_at;
			ALTER TABLE idx_all RENAME COLUMN event_type TO schema_name;
			ALTER TABLE idx_all RENAME COLUMN is_deleted TO deleted;
			ALTER TABLE idx_all RENAME COLUMN expires TO expires_at;
			"""u8,
			multipleStatements: true);

		// Find and rename all secondary index tables
		foreach (var tableNameUtf8 in connection.GetTables()) {
			var tableName = Encoding.UTF8.GetString(tableNameUtf8);

			if (UserIndexSql.IsUserIndexTable(tableName))
				RenameUserIndexColumns(connection, tableName);
		}

		static void RenameUserIndexColumns(DuckDBConnection connection, string tableName) {
			// Add record_id column
			connection.ExecuteAdHocNonQuery(
				$"""
				ALTER TABLE "{tableName}" ADD COLUMN record_id BLOB DEFAULT '';
				ALTER TABLE "{tableName}" RENAME COLUMN event_number TO stream_revision;
				ALTER TABLE "{tableName}" RENAME COLUMN created TO created_at;
				""",
				multipleStatements: true);
		}
	}
}
