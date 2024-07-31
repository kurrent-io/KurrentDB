using EventStore.Streaming;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.System;

class LeadershipAwareTaskMaster : LeadershipAwareService {
    public LeadershipAwareTaskMaster(GetNodeSystemInfo getNodeSystemInfo, INodeLifetimeService nodeLifetime, ILoggerFactory loggerFactory) : base(
        nodeLifetime,
        getNodeSystemInfo,
        loggerFactory
    ) {
        // Logger = loggerFactory.CreateLogger<LeadershipAwareTaskMaster>();
    }

    // ILogger                                 Logger       { get; }
    Dictionary<string, INodeBackgroundTask> TaskRegistry { get; } = [];

    protected override async Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
        foreach (var task in TaskRegistry.Values.Where(x => !x.StartConcurrently)) {
            _ = task.Start(nodeInfo, stoppingToken).ContinueWith(t => {
                if (t.IsFaulted)
                    Logger.LogWarning(t.Exception.Flatten(), "[{TaskName}] Node background task failed to start", task.Name);
                else
                    Logger.LogInformation("[{TaskName}] Node background task started", task.Name);
            });
        }

        await TaskRegistry.Values
            .Where(x => x.StartConcurrently)
            .Select(x => x.Start(nodeInfo, stoppingToken))
            .WhenAll();
    }

    public void RegisterTask(INodeBackgroundTask task) =>
        TaskRegistry.Add(task.Name, task);
}


public interface INodeBackgroundTask {
    string Name              { get; }
    bool   StartConcurrently { get; }
    bool   StopConcurrently  { get; }
    bool   Durable           { get; }

    Task Start(NodeSystemInfo nodeInfo, CancellationToken stoppingToken);
}

// public abstract class ExecutableTask {
//     public string TaskName { get; }
//     public bool   RunOnce  { get; }
//
//     public abstract Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken);
// }