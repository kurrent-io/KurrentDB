#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static KurrentDB.Core.Messages.SystemMessage;

namespace KurrentDB.Core.Hosting.Experimental;

// [PublicAPI]
// public abstract class SystemReadyBackgroundService : BackgroundService {
//     readonly IServiceProvider _services;
//
//     protected SystemReadyBackgroundService(IServiceProvider services) {
//         _services = services;
//         ServiceName = GetType().Name;
//         ReadyWhen   = ReadyWhen.Operational;
//     }
//
//     protected virtual string    ServiceName { get; }
//     protected virtual ReadyWhen ReadyWhen   { get; } 
//
//     protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken) {
//         var logger    = _services.GetRequiredService<ILogger<SystemReadyBackgroundService>>();
//         var probes    = _services.GetRequiredService<SystemReadinessProbeFactory>();
//         var publisher = _services.GetRequiredService<IPublisher>();
//          
//         // Register BEFORE the gate wait. A service that is still waiting to start is then already known
//         // to ShutdownService, so it is asked to stop rather than torn down underneath while it waits.
//         publisher.Publish(new SystemMessage.RegisterForGracefulTermination(ServiceName, () => _ = StopAsync(CancellationToken.None)));
//         
//         try {
//             var nodeInfo = await probes.Create(ReadyWhen).WaitUntilReady(stoppingToken);
//             logger.LogSystemReady(ServiceName);
//             await RunAsync(nodeInfo, stoppingToken);
//         }
//         finally {
//             // Deregister however OnExecuteAsync ended — normal stop, role loss, or fault — so core teardown
//             // stops waiting on us.
//             publisher.Publish(new SystemMessage.ComponentTerminated(ServiceName));
//         }
//     }
//     
//     /// <summary>
//     /// The body of the service, run once the subclass's startup gate opens. The token is cancelled when
//     /// the host stops the service.
//     /// </summary>
//     protected abstract Task RunAsync(NodeSystemInfo nodeInfo, CancellationToken stoppingToken);
// }

//
// [PublicAPI]
// public abstract class SystemReadyBackgroundService(ILogger logger, IPublisher publisher, SystemReadinessProbeFactory probeFactory) : BackgroundService {
//     protected SystemReadyBackgroundService(IServiceProvider services) : this(
//         services.GetRequiredService<ILogger<SystemReadyBackgroundService>>(),
//         services.GetRequiredService<IPublisher>(),
//         services.GetRequiredService<SystemReadinessProbeFactory>()
//     ) { }
//
//     
//     // protected SystemReadyBackgroundService(IServiceProvider services) {
//     //     _logger       = services.GetRequiredService<ILogger<SystemReadyBackgroundService>>();
//     //     _publisher    = services.GetRequiredService<IPublisher>();
//     //     _probeFactory = services.GetRequiredService<SystemReadinessProbeFactory>();
//     // }
//
//     protected abstract string    ServiceName { get; }
//     protected virtual  ReadyWhen ReadyWhen   { get; } = ReadyWhen.Operational;
//
//     protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken) {
//         // Register BEFORE the gate wait. A service that is still waiting to start is then already known
//         // to ShutdownService, so it is asked to stop rather than torn down underneath while it waits.
//         publisher.Publish(new SystemMessage.RegisterForGracefulTermination(ServiceName, () => _ = StopAsync(CancellationToken.None)));
//         
//         try {
//             var nodeInfo = await probeFactory.Create(ReadyWhen).WaitUntilReady(stoppingToken);
//             logger.LogSystemReady(ServiceName);
//             await RunAsync(nodeInfo, stoppingToken);
//         }
//         finally {
//             // Deregister however OnExecuteAsync ended — normal stop, role loss, or fault — so core teardown
//             // stops waiting on us.
//             publisher.Publish(new SystemMessage.ComponentTerminated(ServiceName));
//         }
//     }
//     
//     /// <summary>
//     /// The body of the service, run once the subclass's startup gate opens. The token is cancelled when
//     /// the host stops the service.
//     /// </summary>
//     protected abstract Task RunAsync(NodeSystemInfo nodeInfo, CancellationToken stoppingToken);
// }

// [PublicAPI]
// public abstract class SystemReadyBackgroundService(IServiceProvider services) : BackgroundService {
//     readonly ILogger         _logger       = services.GetRequiredService<ILogger<SystemReadyBackgroundService>>();
//     readonly IPublisher      _publisher    = services.GetRequiredService<IPublisher>();
//     readonly SystemReadiness _probeFactory = services.GetRequiredService<SystemReadiness>();
//
//     protected IServiceProvider Services { get; } = services;
//
//     protected abstract string        ServiceName { get; }
//     protected virtual  NodeReadyWhen ReadyWhen   { get; } = NodeReadyWhen.Operational;
//
//     protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken) {
//         ArgumentException.ThrowIfNullOrWhiteSpace(ServiceName);
//
//         // register BEFORE the gate wait. A service that is still waiting to start is then already known
//         // to ShutdownService, so it is asked to stop rather than torn down underneath while it waits.
//         _publisher.Publish(new SystemMessage.RegisterForGracefulTermination(ServiceName, () => _ = StopAsync(CancellationToken.None)));
//
//         try {
//             var nodeInfo = await _probeFactory.CreateProbe(ReadyWhen).WaitUntilReady(stoppingToken);
//             _logger.LogSystemReady(ServiceName);
//             await RunAsync(nodeInfo, stoppingToken);
//         } finally {
//             // Deregister however OnExecuteAsync ended — normal stop, role loss, or fault — so core teardown
//             // stops waiting on us.
//             _publisher.Publish(new SystemMessage.ComponentTerminated(ServiceName));
//         }
//     }
//
//     /// <summary>
//     /// The body of the service, run once the subclass's startup gate opens. The token is cancelled when
//     /// the host stops the service.
//     /// </summary>
//     protected abstract Task RunAsync(NodeSystemInfo nodeInfo, CancellationToken stoppingToken);
// }

[PublicAPI]
public abstract class SystemReadyBackgroundService : BackgroundService {
    readonly ISystemReadinessProbe _probe;
    readonly ILogger               _logger;
    readonly IPublisher            _publisher;
    
    protected SystemReadyBackgroundService(IServiceProvider services, NodeReadyWhen readyWhen = NodeReadyWhen.Operational, string? serviceName = null) {
        Services = services;
        ReadyWhen = readyWhen;
        ServiceName = serviceName ?? GetType().Name;
        
        _probe = Services
            .GetRequiredService<SystemReadiness>()
            .CreateProbe(ReadyWhen);
        
        _logger    = Services.GetRequiredService<ILogger<SystemReadyBackgroundService>>();
        _publisher = Services.GetRequiredService<IPublisher>();
    }

    protected string           ServiceName { get; }
    protected NodeReadyWhen    ReadyWhen   { get; }
    protected IServiceProvider Services    { get; }

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // Register BEFORE the gate wait, so a service still waiting on readiness is already known
        // to ShutdownService and is asked to stop rather than torn down underneath while it waits.
        _publisher.Publish(new RegisterForGracefulTermination(ServiceName, () => _ = StopAsync(CancellationToken.None)));

        try {
            var nodeInfo = await _probe.WaitUntilReady(stoppingToken);
            _logger.LogSystemReady(ServiceName);
            await RunAsync(nodeInfo, stoppingToken);
        }
        finally {
            // Deregister however OnExecuteAsync ended — normal stop, role loss, or fault — so core teardown
            // stops waiting on us.
            _publisher.Publish(new ComponentTerminated(ServiceName));
        }
    }
    
    /// <summary>
    /// The body of the service, run once the subclass's startup gate opens. The token is cancelled when
    /// the host stops the service.
    /// </summary>
    protected abstract Task RunAsync(NodeSystemInfo nodeInfo, CancellationToken stoppingToken);
}


static partial class SystemReadyBackgroundServiceLogMessages {
    [LoggerMessage(LogLevel.Debug, "System ready, {ServiceName} running")]
    internal static partial void LogSystemReady(this ILogger logger, string serviceName);
}
