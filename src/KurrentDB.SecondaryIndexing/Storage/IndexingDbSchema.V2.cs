// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using DuckDB.NET.Data;
using Kurrent.Quack;
using KurrentDB.SecondaryIndexing.Indexes.User;

namespace KurrentDB.SecondaryIndexing.Storage;

partial class IndexingDbSchema {
	// Gives record_id the same shape a fresh install creates: `blob not null` with no default.
	//
	// V1 could only add the column by giving it a default, since DuckDB cannot add a column and a
	// constraint in one statement ("Adding columns with constraints not yet supported"). So every
	// migrated database ends up with record_id nullable and carrying a default, where 1_Schema.sql and
	// CreateUserIndexNonQuery declare it not null with no default. This reconciles the two.
	//
	// Neither statement rewrites data, so the database file comes out unchanged whatever the size of the
	// index: DuckDB stores validity as its own segment per row group, and one holding no NULLs is
	// already Constant compressed, exactly as it is for a not null column. There is nothing to drop, so
	// SET NOT NULL only has to validate - roughly 60ms per 50M rows. Both statements are no-ops on a
	// column already in this shape, which is how user index tables created after V1B by
	// CreateUserIndexNonQuery are handled.
	//
	// SET NOT NULL is safe because no row can hold a NULL record_id: V1 backfilled every pre-upgrade
	// row with a zero length BLOB, and the appenders always supply the value explicitly
	// (DefaultIndexProcessor, UserIndexProcessor). If one ever did, this fails the migration rather
	// than accepting it.
	private static void UpgradeToV2(DuckDBAdvancedConnection connection) {
		AlignRecordIdWithFreshInstall(connection, "idx_all");

		// Same treatment for every user index table, which V1 gave the same default
		foreach (var tableNameUtf8 in connection.GetTables()) {
			var tableName = Encoding.UTF8.GetString(tableNameUtf8);

			if (UserIndexSql.IsUserIndexTable(tableName))
				AlignRecordIdWithFreshInstall(connection, tableName);
		}

		static void AlignRecordIdWithFreshInstall(DuckDBConnection connection, string tableName) =>
			connection.ExecuteAdHocNonQuery(
				$"""
				 ALTER TABLE "{tableName}" ALTER COLUMN record_id DROP DEFAULT;
				 ALTER TABLE "{tableName}" ALTER COLUMN record_id SET NOT NULL;
				 """,
				multipleStatements: true);
	}
}
