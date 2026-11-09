// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Query;

public interface IDatabaseSchemaProvider {
	/// <summary>
	/// Gets a list of all virtual tables.
	/// </summary>
	IEnumerable<string> Tables { get; }
}
