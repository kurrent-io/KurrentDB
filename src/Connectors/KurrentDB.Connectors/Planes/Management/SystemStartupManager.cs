// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Common.Utils;
using KurrentDB.Connectors.Infrastructure.System.Node;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Connectors.Planes.Management;

internal class SystemStartupManager(IServiceProvider serviceProvider) : BackgroundService, IStartupWorkCompletionMonitor {
	private readonly TaskCompletionSource _completed = TaskCompletionSourceFactory.CreateDefault();

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		var workers = serviceProvider.GetServices<SystemStartupTaskWorker>().ToList();

		if (workers.Count == 0) {
			_completed.TrySetResult();
			return;
		}

		var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		var linked  = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeout.Token);
		var logger  = serviceProvider.GetRequiredService<ILogger<SystemStartupManager>>();

		try {
			logger.LogInformation("System startup tasks started");
			await Task.WhenAll(workers.Select(w => w.ExecuteAsync(linked.Token)));
			logger.LogInformation("System startup tasks completed");
			_completed.TrySetResult();
		} catch (OperationCanceledException ex) when (ex.CancellationToken == linked.Token) {
			_completed.TrySetCanceled(linked.Token);
		} catch (Exception ex) {
			_completed.TrySetException(ex);
		} finally {
			timeout.Dispose();
			linked.Dispose();
		}
	}

	public Task WhenCompletedAsync() {
		return _completed.Task;
	}
}
