// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using DuckDB.NET.Data;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using Kurrent.Quack.Threading;
using KurrentDB.DuckDB;

namespace KurrentDB.SecondaryIndexing.Storage;

public class IndexingDbSchema : DuckDBOneTimeSetup {
	private static readonly Assembly Assembly = typeof(IndexingDbSchema).Assembly;

	protected override void ExecuteCore(DuckDBAdvancedConnection connection) {
		BufferedView.EnableSupport(connection);
		CreateSchema(connection);
	}

	private void CreateSchema(DuckDBConnection connection) {
		var names = Assembly.GetManifestResourceNames().Where(x => x.EndsWith(".sql")).OrderBy(x => x);
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

	public void CreateSchema(DuckDBConnectionPool pool) {
		using (pool.Rent(out var connection)) {
			Execute(connection);
		}
	}
}
