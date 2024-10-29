// ReSharper disable CheckNamespace

using EventStore.Streaming.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventStore.Connect.Schema;

abstract class SchemaRegistryStartupTask : IHostedService {
    protected SchemaRegistryStartupTask(ISchemaRegistry registry, ILogger<SchemaRegistryStartupTask> logger, string? taskName = null) {
        Registry = registry;
        TaskName = (taskName ?? GetType().Name).Replace("StartupTask", "").Replace("Task", "");
        Logger   = logger;
    }

    ISchemaRegistry Registry { get; }
    ILogger         Logger   { get; }
    string          TaskName { get; }

    async Task IHostedService.StartAsync(CancellationToken cancellationToken) {
        try {
            await OnStartup(Registry, cancellationToken);
            Logger.LogDebug("{TaskName} completed", TaskName);
        }
        catch (Exception ex) {
            // Logger.LogError(ex, "{TaskName} failed", TaskName);
            throw new Exception($"Schema registry startup task failed: {TaskName}", ex);
        }
    }

    Task IHostedService.StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected abstract Task OnStartup(ISchemaRegistry registry, CancellationToken cancellationToken);
}

public static class SchemaRegistryStartupTaskExtensions {
    public static IServiceCollection AddSchemaRegistryStartupTask(
        this IServiceCollection services, string taskName, Func<ISchemaRegistry, CancellationToken, Task> onStartup
    ) => services.AddSingleton<IHostedService, FluentSchemaRegistryStartupTask>(ctx =>
        new FluentSchemaRegistryStartupTask(
            taskName, onStartup,
            ctx.GetRequiredService<ISchemaRegistry>(),
            ctx.GetRequiredService<ILogger<SchemaRegistryStartupTask>>())
    );

    class FluentSchemaRegistryStartupTask(
        string taskName,
        Func<ISchemaRegistry, CancellationToken, Task> onStartup,
        ISchemaRegistry registry,
        ILogger<SchemaRegistryStartupTask> logger
    ) : SchemaRegistryStartupTask(registry, logger, taskName) {
        protected override Task OnStartup(ISchemaRegistry registry, CancellationToken cancellationToken) =>
            onStartup(registry, cancellationToken);
    }
}