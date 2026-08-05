// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Core.Hosting;

public abstract class SystemStartupTaskService {
    protected SystemStartupTaskService(IServiceProvider services, string? taskName = null) {
        Services       = services;
        ReadinessProbe = services.GetRequiredService<SystemReadinessProbe>();
        Logger         = services.GetRequiredService<ILogger<SystemStartupTaskService>>();
        TaskName       = taskName ?? GetType().Name.Replace("StartupTask", "").Replace("Task", "");
    }

    IServiceProvider     Services       { get; }
    SystemReadinessProbe ReadinessProbe { get; }
    ILogger              Logger         { get; }
    string               TaskName       { get; }

	public async Task ExecuteAsync(CancellationToken stoppingToken) {
		try {
			var nodeInfo = await ReadinessProbe.WaitUntilReady(stoppingToken);
			await OnStartup(nodeInfo, Services, stoppingToken);
			Logger.LogDebug("{TaskName} completed", TaskName);
		}
		catch (OperationCanceledException) {
			// ignore
		}
		catch (Exception ex) {
			throw new Exception($"System startup task failed: {TaskName}", ex);
		}
	}

	protected abstract Task OnStartup(NodeSystemInfo nodeInfo, IServiceProvider services, CancellationToken ct);
}

public interface ISystemStartupTask {
	Task OnStartup(NodeSystemInfo nodeInfo, IServiceProvider services, CancellationToken ct);
}

public class SystemStartupTaskWorker(string taskName, IServiceProvider services, ISystemStartupTask startupTask)
	: SystemStartupTaskService(services, taskName) {
	protected override Task OnStartup(NodeSystemInfo nodeSystemInfo, IServiceProvider services, CancellationToken ct) =>
		startupTask.OnStartup(nodeSystemInfo, services, ct);
}

public delegate Task OnStartup(NodeSystemInfo nodeInfo, IServiceProvider services, CancellationToken ct);

[PublicAPI]
public static class SystemStartupTasksServiceCollectionExtensions {
	extension(IServiceCollection services) {
        public IServiceCollection AddSystemStartupTask(string taskName, OnStartup onStartup) {
            ArgumentException.ThrowIfNullOrEmpty(taskName);
            services.TryAddSingleton<SystemReadinessProbe>();
            return services.AddSingleton<SystemStartupTaskWorker>(
                ctx => new(taskName, ctx, new SystemStartupTaskProxy(onStartup)));
        }

        public IServiceCollection AddSystemStartupTask<T>(string taskName) where T : class, ISystemStartupTask {
            ArgumentException.ThrowIfNullOrEmpty(taskName);
            services.TryAddSingleton<T>();
            services.TryAddSingleton<SystemReadinessProbe>();
            return services.AddSingleton<SystemStartupTaskWorker>(
                ctx => new(taskName, ctx, ctx.GetRequiredService<T>()));
        }

        public IServiceCollection AddSystemStartupTask<T>() where T : class, ISystemStartupTask =>
            services.AddSystemStartupTask<T>(typeof(T).Name); // typeof(T).Name.Replace("StartupTask", "").Replace("Task", "");
    }

    class SystemStartupTaskProxy(OnStartup onStartup) : ISystemStartupTask {
		public Task OnStartup(NodeSystemInfo nodeInfo, IServiceProvider services, CancellationToken ct) =>
			onStartup(nodeInfo, services, ct);
	}
}
