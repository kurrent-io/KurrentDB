// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Reflection;
using DotNext.Threading;
using Kurrent.Quack.ConnectionPool;

namespace KurrentDB.SecondaryIndexing.Storage;

public class IndexingDbSchema(DuckDBConnectionPool connectionPool) {
	private static readonly Assembly Assembly = typeof(IndexingDbSchema).Assembly;

	private Atomic.Boolean _created;

	public void CreateSchema() {
		if (!_created.FalseToTrue()) {
			return;
		}

		var names = Assembly.GetManifestResourceNames().Where(x => x.EndsWith(".sql")).OrderBy(x => x);
		using var connection = connectionPool.Open();
		using var transaction = connection.BeginTransaction();
		var cmd = connection.CreateCommand();
		cmd.Transaction = transaction;

		try {
			foreach (var name in names) {
				using var stream = Assembly.GetManifestResourceStream(name);
				using var reader = new StreamReader(stream!);
				var script = reader.ReadToEnd();

				cmd.CommandText = script;
				cmd.ExecuteNonQuery();
			}
		} catch {
			transaction.Rollback();
			throw;
		} finally {
			cmd.Dispose();
		}

		transaction.Commit();
	}
}
