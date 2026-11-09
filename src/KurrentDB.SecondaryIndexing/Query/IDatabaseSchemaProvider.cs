// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Apache.Arrow;

namespace KurrentDB.SecondaryIndexing.Query;

public interface IDatabaseSchemaProvider {
	/// <summary>
	/// Gets a list of all virtual tables.
	/// </summary>
	IEnumerable<TableInfo> Tables { get; }

	/// <summary>
	/// Gets the table schema.
	/// </summary>
	/// <param name="table">The table reference.</param>
	/// <returns>Schema of the table.</returns>
	Schema GetTableSchema(TableInfo table);
}
