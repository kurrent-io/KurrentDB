// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Connectors.Infrastructure.System.Node;

abstract class SystemStartupTaskService {
    protected SystemStartupTaskService(IServiceProvider serviceProvider, string? taskName = null) {
        ServiceProvider = serviceProvider;
        ReadinessProbe  = serviceProvider.GetRequiredService<SystemReadinessProbe>();
        Logger          = serviceProvider.GetRequiredService<ILogger<SystemStartupTaskService>>();
        TaskName        = (taskName ?? GetType().Name).Replace("StartupTask", "").Replace("Task", "");
    }

    IServiceProvider     ServiceProvider { get; }
    SystemReadinessProbe ReadinessProbe  { get; }
    ILogger              Logger          { get; }
    string               TaskName        { get; }

    public async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            var nodeInfo = await ReadinessProbe.WaitUntilReady(stoppingToken);
            await OnStartup(nodeInfo, ServiceProvider, stoppingToken);
            Logger.LogDebug("{TaskName} completed", TaskName);
        }
        catch (OperationCanceledException) {
            // ignore
        }
        catch (Exception ex) {
            // Logger.LogError(ex, "{TaskName} failed", TaskName);
            throw new Exception($"System startup task failed: {TaskName}", ex);
        }
    }

    protected abstract Task OnStartup(NodeSystemInfo.NodeSystemInfo nodeInfo, IServiceProvider serviceProvider, CancellationToken stoppingToken);
}

public interface ISystemStartupTask {
    Task OnStartup(NodeSystemInfo.NodeSystemInfo nodeInfo, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}

internal class SystemStartupTaskWorker(string taskName, IServiceProvider serviceProvider, ISystemStartupTask startupTask)
	: SystemStartupTaskService(serviceProvider, taskName) {
	protected override Task OnStartup(NodeSystemInfo.NodeSystemInfo nodeSystemInfo, IServiceProvider serviceProvider, CancellationToken cancellationToken) =>
		startupTask.OnStartup(nodeSystemInfo, serviceProvider, cancellationToken);
}

[PublicAPI]
public static class SystemStartupTasksServiceCollectionExtensions {
    public static IServiceCollection AddSystemStartupTask(
        this IServiceCollection services, string taskName,
        Func<NodeSystemInfo.NodeSystemInfo, IServiceProvider, CancellationToken, Task> onStartup
    ) {
        if (string.IsNullOrWhiteSpace(taskName))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(taskName));

        services.TryAddSingleton<SystemReadinessProbe>();
        return services.AddSingleton<SystemStartupTaskWorker>(
            ctx => new SystemStartupTaskWorker(taskName, ctx, new SystemStartupTaskProxy(onStartup))
        );
    }

    public static IServiceCollection AddSystemStartupTask<T>(this IServiceCollection services, string taskName) where T : class, ISystemStartupTask {
        if (string.IsNullOrWhiteSpace(taskName))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(taskName));

        services.TryAddSingleton<T>();
        services.TryAddSingleton<SystemReadinessProbe>();
        return services.AddSingleton<SystemStartupTaskWorker>(
            ctx => new SystemStartupTaskWorker(taskName, ctx, ctx.GetRequiredService<T>())
        );
    }

    public static IServiceCollection AddSystemStartupTask<T>(this IServiceCollection services) where T : class, ISystemStartupTask =>
        AddSystemStartupTask<T>(services, typeof(T).Name);

    class SystemStartupTaskProxy(Func<NodeSystemInfo.NodeSystemInfo, IServiceProvider, CancellationToken, Task> onStartup) : ISystemStartupTask {
        public Task OnStartup(NodeSystemInfo.NodeSystemInfo nodeInfo, IServiceProvider serviceProvider, CancellationToken cancellationToken) =>
            onStartup(nodeInfo, serviceProvider, cancellationToken);
    }
}
