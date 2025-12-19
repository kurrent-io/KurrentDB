// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack.ConnectionPool;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using KurrentDB.DuckDB;

namespace KurrentDB.Core.DuckDB;

public static class InjectionExtensions {
	public static IServiceCollection AddDuckInfra(this IServiceCollection services) {
		services.AddSingleton<DuckDBConnectionPoolLifetime>();
		services.AddDuckDBSetup<KdbGetEventSetup>();
		services.AddSingleton<DuckDBConnectionPool>(sp => sp.GetRequiredService<DuckDBConnectionPoolLifetime>().Shared);

		return services;
	}

	// Attaches a duck connection pool (duck pond??) to the kestrel connection because issuing secondary index reads will
	// build query plans and cache them on the (pooled) duckdb connection and we dont want to end up with too many of these over time.
	// If a pool is not attached to the connection the reading infra will use the shared pool.
	public static void UseDuckDbConnectionPoolPerConnection(this ListenOptions listenOptions) {
		listenOptions.Use(next => async connectionContext => {
			var poolFactory = listenOptions.ApplicationServices.GetRequiredService<DuckDBConnectionPoolLifetime>();
			using var pool = poolFactory.CreatePool();
			connectionContext.Items[nameof(DuckDBConnectionPool)] = pool;
			await next(connectionContext);
		});
	}

	[CanBeNull]
	public static DuckDBConnectionPool GetDuckDbConnectionPool(this HttpContext httpContext) {
		var connectionItemsFeature = httpContext.Features.Get<IConnectionItemsFeature>();

		if (connectionItemsFeature is null ||
			!connectionItemsFeature.Items.TryGetValue(nameof(DuckDBConnectionPool), out var item))
			return null;

		return item as DuckDBConnectionPool;
	}
}
