// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Core.Hosting;

public class SystemStartupManager(IServiceProvider serviceProvider) : BackgroundService, IStartupWorkCompletionMonitor {
    readonly TaskCompletionSource _completed = new();

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var logger  = serviceProvider.GetRequiredService<ILogger<SystemStartupManager>>();
        var workers = serviceProvider.GetServices<SystemStartupTaskWorker>().ToList();
        
		if (workers.Count == 0) {
			_completed.TrySetResult();
			return;
		}
        
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linked  = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeout.Token);
		
        var linkedToken = linked.Token;
        
		try {
			logger.LogInformation("System startup tasks started");
			await Task.WhenAll(workers.Select(w => w.ExecuteAsync(linkedToken)));
			logger.LogInformation("System startup tasks completed");
			_completed.TrySetResult();
		} catch (OperationCanceledException ex) when (ex.CancellationToken == linked.Token) {
			_completed.TrySetCanceled(linked.Token);
		} catch (Exception ex) {
			_completed.TrySetException(ex);
		}
	}

	public Task WhenCompletedAsync() => _completed.Task;
}
