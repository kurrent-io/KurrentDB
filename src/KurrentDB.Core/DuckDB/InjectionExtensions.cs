// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.DuckDB;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Core.DuckDB;

public static class InjectionExtensions {
	public static IServiceCollection AddDuckInfra(this IServiceCollection services) {
		services.AddSingleton<DuckDBConnectionPoolLifetime>();
		services.AddDuckDBSetup<KdbGetEventSetup>();
		services.AddSingleton<DuckDBConnectionPool>(sp => sp.GetRequiredService<DuckDBConnectionPoolLifetime>().Shared);
		services.AddDuckDbConnectionScopedPools();
		return services;
	}

	public static IServiceCollection AddDuckDbConnectionScopedPools(this IServiceCollection services) {
		services.AddHttpContextAccessor();
		services.AddScoped(sp => sp
			.GetRequiredService<IHttpContextAccessor>()
			.HttpContext?
			.GetConnectionScopedDuckDbConnectionPool());

		return services;
	}

	// Attaches a duck connection pool (duck pond??) to the kestrel connection because issuing secondary index reads will
	// build query plans and cache them on the (pooled) duckdb connection and we dont want to end up with too many of these over time.
	// If a pool is not attached to the connection the reading infra will use the shared pool.
	public static void UseDuckDbConnectionPoolPerConnection(this ListenOptions listenOptions) {
		listenOptions.Use(next => async connectionContext => {
			var poolFactory = listenOptions.ApplicationServices.GetRequiredService<DuckDBConnectionPoolLifetime>();
			// pool is disposed when the connection closes
			var lazyPool = new Lazy<DuckDBConnectionPool>(poolFactory.CreatePool);
			try {
				// scoped wrapper is added to the context so that the scoped pool can be easily requested as opposed to the shared pool 
				connectionContext.Items[nameof(ConnectionScopedDuckDBConnectionPool)] = new ConnectionScopedDuckDBConnectionPool(lazyPool);
				await next(connectionContext);
			} finally {
				// synchronize to avoid possibility of value being created after we check IsValueCreated
				lock (lazyPool) {
					if (lazyPool.IsValueCreated) {
						lazyPool.Value.Dispose();
					}
				}
			}
		});
	}

	public static ConnectionScopedDuckDBConnectionPool GetConnectionScopedDuckDbConnectionPool(this HttpContext httpContext) {
		var connectionItemsFeature = httpContext.Features.Get<IConnectionItemsFeature>();

		if (connectionItemsFeature is not null &&
			connectionItemsFeature.Items.TryGetValue(nameof(ConnectionScopedDuckDBConnectionPool), out var item) &&
			item is ConnectionScopedDuckDBConnectionPool pool)
			return pool;

		throw new InvalidOperationException($"No {nameof(ConnectionScopedDuckDBConnectionPool)} is available for this connection");
	}
}
