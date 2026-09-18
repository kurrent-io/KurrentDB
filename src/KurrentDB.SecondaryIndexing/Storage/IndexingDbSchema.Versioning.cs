// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Storage;

// DATABASE MIGRATION GUIDE:
// 1. Add migration action to MigrationActions with appropriate version
// 2. Modify DDL (*.sql files or in-place SQL statements)
// 3. Bump TargetVersion constant below
//
// Do not edit migrations that have been shipped.
// If a migration must be patched, create a new one with B/C/D suffix and ensure all upgrade
// path variations are covered in MigrationTests.SchemaParity
partial class IndexingDbSchema {
	internal const int TargetVersion = 2;

	internal static SortedDictionary<int, Action<DuckDBAdvancedConnection>> MigrationActions
		=> new() {
			{ 1, UpgradeToV1B }, // V1 renamed to V1A and replaced with V1B
			{ 2, UpgradeToV2 },
		};
}
