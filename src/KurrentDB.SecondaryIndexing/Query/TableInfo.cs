// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Query;

public readonly record struct TableInfo(string Schema, string Name) {
	public string FullName => $"{Schema}.{Name}";

	public override string ToString() => FullName;
}
