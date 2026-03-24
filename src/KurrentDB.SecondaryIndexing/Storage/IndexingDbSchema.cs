// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using System.Security.Claims;
using Kurrent.Quack;
using Kurrent.Quack.Threading;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.DuckDB;
using Microsoft.Extensions.Logging;

namespace KurrentDB.SecondaryIndexing.Storage;

public partial class IndexingDbSchema(
	Func<long[], ClaimsPrincipal, IEnumerator<ReadResponse>> eventsProvider,
	ILoggerFactory? loggerFactory = null) : DuckDBOneTimeSetup {
	private static readonly Assembly Assembly = typeof(IndexingDbSchema).Assembly;

	public IndexingDbSchema(IPublisher publisher)
		: this(publisher.GetEnumerator) {
	}

	protected override void ExecuteCore(DuckDBAdvancedConnection connection) {
		BufferedView.EnableSupport(connection);
		new Indexes.User.ExpandRecordFunction(eventsProvider).Register(connection);
		new Indexes.Default.ExpandRecordFunction(eventsProvider).Register(connection);

		PerformMigration(connection, logger: loggerFactory);
	}

	private static void CreateSchema(DuckDBAdvancedConnection connection) {
		var names = Assembly.GetManifestResourceNames().Where(x => x.EndsWith(".sql")).OrderBy(x => x);
		using var transaction = connection.BeginTransaction();

		foreach (var name in names) {
			using var stream = Assembly.GetManifestResourceStream(name);
			using var reader = new StreamReader(stream!);
			var script = reader.ReadToEnd();

			connection.ExecuteAdHocNonQuery(script, multipleStatements: true);
		}
	}
}

file static class EventReader {
	public static IEnumerator<ReadResponse> GetEnumerator(this IPublisher publisher, long[] logPositions, ClaimsPrincipal user)
		=> new Enumerator.ReadLogEventsSync(
			bus: publisher,
			logPositions,
			user);
}
