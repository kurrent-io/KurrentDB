// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using System.Security.Claims;
using DuckDB.NET.Data;
using Kurrent.Quack;
using Kurrent.Quack.Threading;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.DuckDB;

namespace KurrentDB.SecondaryIndexing.Storage;

public class IndexingDbSchema(Func<long[], ClaimsPrincipal, IEnumerator<ReadResponse>> eventsProvider) : DuckDBOneTimeSetup {
	private static readonly Assembly Assembly = typeof(IndexingDbSchema).Assembly;

	public IndexingDbSchema(IPublisher publisher)
		: this(publisher.GetEnumerator) {
	}

	protected override void ExecuteCore(DuckDBAdvancedConnection connection) {
		BufferedView.EnableSupport(connection);
		new Indexes.User.ExpandRecordFunction(eventsProvider).Register(connection);
		new Indexes.Default.ExpandRecordFunction(eventsProvider).Register(connection);
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
}

file static class EventReader {
	public static IEnumerator<ReadResponse> GetEnumerator(this IPublisher publisher, long[] logPositions, ClaimsPrincipal user)
		=> new Enumerator.ReadLogEventsSync(
			bus: publisher,
			logPositions,
			user);
}
