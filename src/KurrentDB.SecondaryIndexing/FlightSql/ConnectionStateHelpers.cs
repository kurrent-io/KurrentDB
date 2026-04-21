// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Apache.Arrow.Flight.Server;
using KurrentDB.Core;
using KurrentDB.SecondaryIndexing.Query;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SecondaryIndexing.FlightSql;

internal static class ConnectionStateHelpers {
	public static IServiceCollection AddFlightSqlServer(this IServiceCollection services)
		=> services
			.AddSingleton<FlightServer, FlightSqlServer>()
			.AddSingleton<ConnectionInterceptor>(SetupConnectionState);

	private static async Task SetupConnectionState(this IQueryEngine engine, ConnectionDelegate next, ConnectionContext context) {
		using var state = new ConnectionState(engine);
		context.Features.Set(state);
		await next(context);
	}

	private static ConnectionInterceptor SetupConnectionState(this IServiceProvider provider)
		=> provider.GetRequiredService<IQueryEngine>().SetupConnectionState;
}
