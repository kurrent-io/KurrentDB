// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Query;

partial class QueryEngine : IDatabaseSchemaProvider {
	IEnumerable<string> IDatabaseSchemaProvider.Tables => userIndex.Indices
		.Select(static indexName => string.Concat(UserIndicesSchema, ".", indexName))
		.Append(DefaultIndexTableFullName);
}
