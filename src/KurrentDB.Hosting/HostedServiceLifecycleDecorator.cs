// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Time;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace KurrentDB;

// Decorates every registered IHostedService and logs entry/exit/duration for
// StartAsync and StopAsync, so a slow or stalling service stands out during shutdown.
// Use Decorate to choose the decorator: a BackgroundService must stay a BackgroundService,
// or the host stops watching its ExecuteTask (see BackgroundServiceLifecycleDecorator).
public sealed class HostedServiceLifecycleDecorator(IHostedService inner) : IHostedService {
	static readonly ILogger Log = Serilog.Log.ForContext<HostedServiceLifecycleDecorator>();

	public static IHostedService Decorate(IHostedService inner) =>
		inner is BackgroundService backgroundService
			? new BackgroundServiceLifecycleDecorator(backgroundService)
			: new HostedServiceLifecycleDecorator(inner);

	readonly string _name = inner.GetType().Name;

	public async Task StartAsync(CancellationToken cancellationToken) {
		var start = Instant.Now;
		Log.Debug("Starting {Service}...", _name);
		try {
			await inner.StartAsync(cancellationToken);
			Log.Debug("Started {Service} in {Elapsed} ms",
				_name,
				Instant.Now.ElapsedTimeSince(start).TotalMilliseconds);
		} catch (Exception ex) {
			Log.Debug("Failed to start {Service} after {Elapsed} ms. {Message}",
				_name,
				Instant.Now.ElapsedTimeSince(start).TotalMilliseconds,
				ex.Message);
			throw;
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken) {
		var start = Instant.Now;
		Log.Information("Stopping {Service}...", _name);
		try {
			await inner.StopAsync(cancellationToken);
			Log.Information("Stopped {Service} in {Elapsed} ms",
				_name,
				Instant.Now.ElapsedTimeSince(start).TotalMilliseconds);
		} catch (Exception ex) {
			Log.Information("Failed to stop {Service} after {Elapsed} ms. {Message}",
				_name,
				Instant.Now.ElapsedTimeSince(start).TotalMilliseconds,
				ex.Message);
			throw;
		}
	}
}

// The host checks each started service for BackgroundService and watches its ExecuteTask:
// when that task faults it logs the failure and applies HostOptions.BackgroundServiceExceptionBehavior.
// Deriving from BackgroundService and passing ExecuteTask through keeps that working behind the decorator.
public sealed class BackgroundServiceLifecycleDecorator(BackgroundService inner) : BackgroundService {
	readonly HostedServiceLifecycleDecorator _logging = new(inner);

	public override Task? ExecuteTask =>
		inner.ExecuteTask;

	public override Task StartAsync(CancellationToken cancellationToken) =>
		_logging.StartAsync(cancellationToken);

	public override Task StopAsync(CancellationToken cancellationToken) =>
		_logging.StopAsync(cancellationToken);

	// StartAsync is overridden and never calls base, so this is never invoked.
	protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
		throw new InvalidOperationException("Unexpected error, this should never be called");
}
