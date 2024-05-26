using EventStore.Connectors.ControlPlane.Contracts.Activation;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.ControlPlane.Activation;

/// <summary>
/// Activates or deactivates connectors on request.
/// </summary>
public class ConnectorsActivator : ControlPlaneProcessingModule {
    public ConnectorsActivator(ConnectorsTaskManager taskManager, TimeProvider time) {
        ProcessOnNode<ActivateConnectors>(
            evt => evt.NodeId,
            async (evt, ctx) => {
                var results = new List<ConnectorsTaskManager.ConnectorProcessInfo>();

                foreach (var connector in evt.Connectors) {
                    var result = await taskManager.StartProcess(
                        connector.ConnectorId, 
                        connector.Revision, 
                        new(connector.Settings.ToDictionary()), 
                        ctx.CancellationToken
                    );
                    
                    ctx.Logger.LogProcessStarted(evt.NodeId, connector.ConnectorId);
                    
                    results.Add(result);
                }
                
                ctx.Output(
                    new ConnectorsActivated {
                        ActivationId = evt.ActivationId,
                        NodeId       = evt.NodeId,
                        Connectors   = { results.MapToConnectors() },
                        ActivatedAt  = time.MapToUtcNowTimestamp(),
                    }
                );
            }
        );

        ProcessOnNode<DeactivateConnectors>(
            evt => evt.NodeId,
            async (evt, ctx) => {
                var results = new List<ConnectorsTaskManager.ConnectorProcessInfo>();

                foreach (var connector in evt.Connectors) {
                    var result = await taskManager.StopProcess(
                        connector.ConnectorId, 
                        ctx.CancellationToken
                    );
                    
                    ctx.Logger.LogProcessStopped(result.Error, evt.NodeId, connector.ConnectorId);
                    
                    results.Add(result);
                }

                ctx.Output(
                    new ConnectorsDeactivated {
                        ActivationId  = evt.ActivationId,
                        NodeId        = evt.NodeId,
                        Connectors    = { results.MapToConnectors() },
                        DeactivatedAt = time.MapToUtcNowTimestamp(),
                    }
                );
            }
        );
    }
}

static partial class ConnectorsActivatorLogMessages {
    [LoggerMessage(
        Message = "[Node Id: {NodeId}] connector {ConnectorId} process started",
        Level   = LogLevel.Debug
    )]
    internal static partial void LogProcessStarted(
        this ILogger logger, string nodeId, string connectorId
    );

    [LoggerMessage(
        Message = "[Node Id: {NodeId}] connector {ConnectorId} process stopped",
        Level   = LogLevel.Debug
    )]
    internal static partial void LogProcessStopped(
        this ILogger logger, Exception? error, string nodeId, string connectorId
    );

    [LoggerMessage(
        Message = "[Node Id: {NodeId}] connector {ConnectorId} process failed",
        Level   = LogLevel.Error
    )]
    internal static partial void LogProcessFailed(
        this ILogger logger, Exception? error, string nodeId, string connectorId
    );
}