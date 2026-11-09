// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Query;

partial class QueryEngine {
	private const string SystemSchema = "kdb";
	private const string UserIndicesSchema = "usr";
	private const string DefaultIndexTableName = "records";

	IEnumerable<TableInfo> IDatabaseSchemaProvider.Tables => userIndex.Indices
		.Select(static indexName => new TableInfo(UserIndicesSchema, indexName))
		.Append(new(SystemSchema, DefaultIndexTableName));
}
