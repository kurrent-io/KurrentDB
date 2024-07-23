// ReSharper disable CheckNamespace

using EventStore.Streaming.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EventStore.Connect.Schema;

public abstract class SchemaRegistryStartupTask : IHostedService {
    protected SchemaRegistryStartupTask(ISchemaRegistry registry, string? taskName = null) {
        Registry = registry;
        TaskName = (taskName ?? GetType().Name).Replace("StartupTask", "").Replace("Task", "");
    }

    ISchemaRegistry Registry { get; }
    string          TaskName { get; }

    async Task IHostedService.StartAsync(CancellationToken cancellationToken) {
        try {
            await OnStartup(Registry, cancellationToken);
        }
        catch (Exception ex) {
            throw new Exception($"Schema registry startup task failed: {TaskName}", ex);
        }
    }

    Task IHostedService.StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected abstract Task OnStartup(ISchemaRegistry registry, CancellationToken cancellationToken);
}

public static class SchemaRegistryStartupTaskExtensions {
    public static IServiceCollection AddSchemaRegistryStartupTask(
        this IServiceCollection services, string taskName, Func<ISchemaRegistry, CancellationToken, Task> onStartup
    ) => services.AddHostedService(ctx => new FluentSchemaRegistryStartupTask(taskName, onStartup, ctx.GetRequiredService<ISchemaRegistry>()));

    class FluentSchemaRegistryStartupTask(string taskName, Func<ISchemaRegistry, CancellationToken, Task> onStartup, ISchemaRegistry registry)
        : SchemaRegistryStartupTask(registry, taskName) {
        protected override Task OnStartup(ISchemaRegistry registry, CancellationToken cancellationToken) =>
            onStartup(registry, cancellationToken);
    }
}