// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using DuckDB.NET.Data;
using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Indexes.User;

namespace KurrentDB.SecondaryIndexing.Storage;

partial class IndexingDbSchema {
	// The V1 migration exactly as it originally shipped.
	// It is superseded in V1B and deliberately NOT registered in MigrationActions
	// This is kept in its original form so that we can test the migration path
	// that some databases will have taken through it.
	internal static void UpgradeToV1A(DuckDBAdvancedConnection connection) {
		// Add record_id column and rename columns
		connection.ExecuteAdHocNonQuery("""
		                                CREATE TABLE idx_metadata(key varchar primary key not null, value varchar);
		                                ALTER TABLE idx_all ADD COLUMN record_id BLOB DEFAULT ''::BLOB;
		                                ALTER TABLE idx_all RENAME COLUMN event_number TO stream_revision;
		                                ALTER TABLE idx_all RENAME COLUMN created TO created_at;
		                                ALTER TABLE idx_all RENAME COLUMN event_type TO schema_name;
		                                ALTER TABLE idx_all RENAME COLUMN is_deleted TO deleted;
		                                ALTER TABLE idx_all RENAME COLUMN expires TO expires_at;
		                                """u8, multipleStatements: true);

		// Find and rename all secondary index tables
		foreach (var tableNameUtf8 in connection.GetTables()) {
			var tableName = Encoding.UTF8.GetString(tableNameUtf8);

			if (UserIndexSql.IsUserIndexTable(tableName))
				RenameUserIndexColumns(connection, tableName);
		}

		static void RenameUserIndexColumns(DuckDBConnection connection, string tableName) {
			// Add record_id column
			connection.ExecuteAdHocNonQuery($"""
			                                 ALTER TABLE "{tableName}" ADD COLUMN record_id BLOB DEFAULT ''::BLOB;
			                                 ALTER TABLE "{tableName}" RENAME COLUMN event_number TO stream_revision;
			                                 ALTER TABLE "{tableName}" RENAME COLUMN created TO created_at;
			                                 """, multipleStatements: true);
		}
	}
}
