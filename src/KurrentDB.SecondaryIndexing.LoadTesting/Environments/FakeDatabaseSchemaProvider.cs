// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.Query;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments;

internal sealed class FakeDatabaseSchemaProvider : IDatabaseSchemaProvider {
	IEnumerable<TableInfo> IDatabaseSchemaProvider.Tables => [];
}
