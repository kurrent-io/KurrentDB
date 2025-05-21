// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.Indices.Default;

internal static class Sql {
	public const string AppendIndexSql = "insert or ignore into {table} (id, name) values ($id, $name);";
}
