using Kurrent.Surge.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KurrentDB.SchemaRegistry.Infrastructure;

abstract class SchemaMessageRegistrationStartupTask : IHostedService {
    protected SchemaMessageRegistrationStartupTask(ISchemaRegistry client, ILogger<SchemaMessageRegistrationStartupTask> logger, string? taskName = null) {
        Client   = client;
        TaskName = (taskName ?? GetType().Name).Replace("StartupTask", "").Replace("Task", "");
        Logger   = logger;
    }

    ISchemaRegistry Client   { get; }
    ILogger         Logger   { get; }
    string          TaskName { get; }

    async Task IHostedService.StartAsync(CancellationToken cancellationToken) {
        try {
            await OnStartup(Client, cancellationToken);
            Logger.LogDebug("{TaskName} completed", TaskName);
        }
        catch (Exception ex) {
            // Logger.LogError(ex, "{TaskName} failed", TaskName);
            throw new($"Schema message registration registration startup task failed: {TaskName}", ex);
        }
    }

    Task IHostedService.StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected abstract Task OnStartup(ISchemaRegistry registry, CancellationToken cancellationToken);
}

public static class SchemaRegistryStartupTaskExtensions {
    public static IServiceCollection AddSchemaMessageRegistrationStartupTask(
        this IServiceCollection services, string taskName, Func<ISchemaRegistry, CancellationToken, Task> onStartup
    ) => services.AddSingleton<IHostedService, FluentSchemaMessageRegistrationStartupTask>(ctx => new(
            taskName, onStartup,
            ctx.GetRequiredService<ISchemaRegistry>(),
            ctx.GetRequiredService<ILogger<SchemaMessageRegistrationStartupTask>>())
    );

    class FluentSchemaMessageRegistrationStartupTask(
        string taskName,
        Func<ISchemaRegistry, CancellationToken, Task> onStartup,
        ISchemaRegistry client,
        ILogger<SchemaMessageRegistrationStartupTask> logger
    ) : SchemaMessageRegistrationStartupTask(client, logger, taskName) {
        protected override Task OnStartup(ISchemaRegistry registry, CancellationToken cancellationToken) =>
            onStartup(registry, cancellationToken);
    }
}