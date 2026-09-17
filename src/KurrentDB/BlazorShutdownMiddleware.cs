// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace KurrentDB;

// Stops the embedded UI from re-establishing its circuit while the server is shutting down.
//
// This is a workaround for an open ASP.NET Core issue — remove it once SignalR stops accepting connections
// itself: https://github.com/dotnet/aspnetcore/issues/58947
//
// Without it the circuit is re-established and not closed by the shutdown procedure, which times out and
// transitions to a forced shutdown.
public sealed class BlazorShutdownMiddleware(RequestDelegate next, IHostApplicationLifetime lifetime) {
	static readonly ILogger Log = Serilog.Log.ForContext<BlazorShutdownMiddleware>();

	// Covers both /_blazor/negotiate and the /_blazor transport request.
	const string CircuitPath = "/_blazor";

	public Task Invoke(HttpContext context) {
		if (!context.Request.Path.StartsWithSegments(CircuitPath, StringComparison.OrdinalIgnoreCase))
			return next(context);

		if (!lifetime.ApplicationStopping.IsCancellationRequested)
			return InvokeCircuit(context);

		Log.Debug("Refusing a circuit request during shutdown");
		context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
		return Task.CompletedTask;
	}

	async Task InvokeCircuit(HttpContext context) {
		// Cancelling RequestAborted is not enough
		using var registration = lifetime.ApplicationStopping.Register(() => {
			Log.Debug("Aborting a circuit connection that was open when the shutdown began");
			context.Abort();
		});
		await next(context);
	}
}
