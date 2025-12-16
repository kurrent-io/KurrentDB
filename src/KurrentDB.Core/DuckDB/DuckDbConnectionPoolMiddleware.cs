// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using Kurrent.Quack.ConnectionPool;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace KurrentDB.Core.DuckDB;

// Attaches a (lazy) duck connection pool to the kestrel connection because issuing secondary index reads will
// build query plans and cache them on the (pooled) duckdb connection and we dont want to end up with too many of these over time.
// If a pool is not attached to the connection the reading infra will use the shared pool.
// It's lazy because not all connections will make use of duck.
public class DuckDbConnectionPoolMiddleware(DuckDBConnectionPoolLifetime duckDbConnectionPoolLifetime) : IMiddleware {
	public const string Key = "DuckDbConnectionPool";

	private static readonly ILogger Log = Serilog.Log.ForContext<DuckDbConnectionPoolMiddleware>();

	public Task InvokeAsync(HttpContext context, RequestDelegate next) {
		var itemsFeature = context.Features.Get<IConnectionItemsFeature>();
		var lifetimeFeature = context.Features.Get<IConnectionLifetimeFeature>();
		var idFeature = context.Features.Get<IConnectionIdFeature>();

		if (itemsFeature is null)
			return MissingFeature(nameof(IConnectionItemsFeature));

		if (lifetimeFeature is null)
			return MissingFeature(nameof(IConnectionLifetimeFeature));

		if (idFeature is null)
			return MissingFeature(nameof(IConnectionIdFeature));

		var id = idFeature.ConnectionId;
		lock (itemsFeature.Items) {
			if (itemsFeature.Items.ContainsKey(Key))
				return next(context);

			Log.Verbose("Creating lazy DuckDB connection pool for connection ID: {connectionId}", id);
			var pool = new Lazy<DuckDBConnectionPool>(duckDbConnectionPoolLifetime.CreatePool);
			lifetimeFeature.ConnectionClosed.Register(DisposePool);
			itemsFeature.Items[Key] = pool;

			void DisposePool() {
				Log.Verbose("Disposing DuckDB connection pool for connection ID: {connectionId}", id);

				if (pool.IsValueCreated)
					pool.Value.Dispose();
			}
		}

		return next(context);

		Task MissingFeature(string feature) {
			Log.Warning("Failed to get connection feature: {feature}. DuckDB query performance will degrade.", feature);
			return next(context);
		}
	}
}
