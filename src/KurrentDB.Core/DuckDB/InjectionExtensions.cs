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

	/// <summary>
	/// Gets the DuckDB connection pool associated with the lifetime of this connection
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown if no DuckDB connection pool is associated with the current connection
	/// </exception>
	public static ConnectionScopedDuckDBConnectionPool GetConnectionScopedDuckDbConnectionPool(this HttpContext httpContext) {
		var connectionItemsFeature = httpContext.Features.Get<IConnectionItemsFeature>();

		if (connectionItemsFeature is not null &&
			connectionItemsFeature.Items.TryGetValue(nameof(ConnectionScopedDuckDBConnectionPool), out var item) &&
			item is ConnectionScopedDuckDBConnectionPool pool)
			return pool;

		throw new InvalidOperationException($"No {nameof(ConnectionScopedDuckDBConnectionPool)} is available for this connection");
	}

	/// <summary>
	/// Configures a dedicated DuckDB connection pool for each Kestrel connection.
	/// </summary>
	/// <remarks>
	/// Attaching a pool to individual connections allows query plans to be cached on pooled DuckDB connections
	/// without accumulating too many cached plans over time.
	/// </remarks>
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
}
