// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Time;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace KurrentDB;

// Decorates every registered IHostedService and logs entry/exit/duration for
// StartAsync and StopAsync, so a slow or stalling service stands out during shutdown.
public sealed class HostedServiceLifecycleDecorator(IHostedService inner) : IHostedService {
	static readonly ILogger Log = Serilog.Log.ForContext<HostedServiceLifecycleDecorator>();

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
