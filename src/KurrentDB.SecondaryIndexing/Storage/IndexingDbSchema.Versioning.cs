// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Storage;

// DATABASE MIGRATION GUIDE:
// 1. Add migration action to MigrationActions with appropriate version
// 2. Modify DDL (*.sql files or in-place SQL statements)
// 3. Dump DesiredVersion constant
partial class IndexingDbSchema {
	private const int TargetVersion = 1;

	private static SortedDictionary<int, Action<DuckDBAdvancedConnection>> MigrationActions
		=> new() {
			{ 0, static _ => { } },
			{ 1, UpgradeToV1 }
		};
}
