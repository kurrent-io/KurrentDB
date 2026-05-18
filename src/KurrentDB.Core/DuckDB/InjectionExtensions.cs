// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.DuckDB;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Core.DuckDB;

public static class InjectionExtensions {
	public static IServiceCollection AddDuckDb(this IServiceCollection services) {
		services.AddSingleton<DuckDBConnectionPoolLifetime>();
		services.AddHostedService(sp => sp.GetRequiredService<DuckDBConnectionPoolLifetime>());
		services.AddSingleton<DuckDBConnectionPool>(sp => sp.GetRequiredService<DuckDBConnectionPoolLifetime>().Shared);
		services.AddSingleton<DuckDbConnectionPoolMiddleware>();
		services.AddSingleton<ConnectionInterceptor>(CreatePoolPerConnectionInterceptor);
		return services;
	}

	private static ConnectionInterceptor CreatePoolPerConnectionInterceptor(IServiceProvider provider)
		=> provider.InjectPoolPerConnectionAsync;

	private static async Task InjectPoolPerConnectionAsync(this IServiceProvider services,
		ConnectionDelegate next,
		ConnectionContext context) {
		// pool is disposed when the connection closes
		var poolFactory = services.GetRequiredService<DuckDBConnectionPoolLifetime>();
		using var pool = new ConnectionScopedDuckDBConnectionPool(poolFactory);
		context.Features.Set<ConnectionScopedDuckDBConnectionPool>(pool);
		await next(context);
		// guaranteed no request handlers are running when the pool wrapper is disposed
	}

	public static IApplicationBuilder UseDuckDb(this IApplicationBuilder app) {
		app.UseMiddleware<DuckDbConnectionPoolMiddleware>();
		return app;
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
