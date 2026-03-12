// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using Kurrent.Quack;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.DuckDB;

namespace KurrentDB.Core.DuckDB;

public class KdbGetEventSetup(IPublisher publisher) : DuckDBOneTimeSetup {
	protected override void ExecuteCore(DuckDBAdvancedConnection connection)
		=> Register(connection, publisher.GetEnumerator);

	public static void Register(DuckDBAdvancedConnection connection,
		Func<long[], ClaimsPrincipal, IEnumerator<ReadResponse>> eventsProvider)
		=> new GetDatabaseEventsFunction(eventsProvider).Register(connection);
}


file static class EventReader {
	public static IEnumerator<ReadResponse> GetEnumerator(this IPublisher publisher, long[] logPositions, ClaimsPrincipal user)
		=> new Enumerator.ReadLogEventsSync(
			bus: publisher,
			logPositions,
			user);
}
