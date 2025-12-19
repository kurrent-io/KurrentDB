// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.DuckDB;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Core.DuckDB;

public static class InjectionExtensions {
	public static IServiceCollection AddDuckInfra(this IServiceCollection services) {
		services.AddSingleton<DuckDBConnectionPoolLifetime>();
		services.AddHostedService(sp => sp.GetRequiredService<DuckDBConnectionPoolLifetime>());
		services.AddDuckDBSetup<KdbGetEventSetup>();
		services.AddSingleton<DuckDBConnectionPool>(sp => sp.GetRequiredService<DuckDBConnectionPoolLifetime>().Shared);
		services.AddSingleton<DuckDbConnectionPoolMiddleware>();
		return services;
	}

	public static IApplicationBuilder UseDuckDbConnectionPool(this IApplicationBuilder app) {
		app.UseMiddleware<DuckDbConnectionPoolMiddleware>();
		return app;
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
			// pool is disposed when the connection closes
			var poolFactory = listenOptions.ApplicationServices.GetRequiredService<DuckDBConnectionPoolLifetime>();
			using var pool = new ConnectionScopedDuckDBConnectionPool(poolFactory);
			connectionContext.Features.Set<ConnectionScopedDuckDBConnectionPool>(pool);
			await next(connectionContext);
			// guaranteed no request handlers are running when the pool wrapper is disposed
		});
	}
}

file class DuckDbConnectionPoolMiddleware : IMiddleware {
	public Task InvokeAsync(HttpContext context, RequestDelegate next) {
		context.RequestServices = new DuckDBConnectionPoolProvider(
			context.Features,
			context.RequestServices);
		return next(context);
	}

	sealed class DuckDBConnectionPoolProvider(IFeatureCollection features, IServiceProvider inner) : IServiceProvider {
		public object GetService(Type serviceType) =>
			serviceType == typeof(DuckDBConnectionPool)
				? features.Get<ConnectionScopedDuckDBConnectionPool>().GetPool()
				: inner.GetService(serviceType);
	}
}
