using EventStore.Connectors.Control.Contracts.Activation;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Control.Activation;

/// <summary>
/// Activates or deactivates connectors.
/// </summary>
public class ConnectorsActivationModule : ControlPlaneProcessingModule {
    public ConnectorsActivationModule(ConnectorsActivator activator) {
        ProcessOnNode<ActivateConnectors>(
            cmd => cmd.NodeId,
            async (cmd, ctx) => {
                var result = await activator.Activate(cmd, ctx.CancellationToken);
                ctx.Output(result);
            }
        );

        ProcessOnNode<DeactivateConnectors>(
            cmd => cmd.NodeId,
            async (cmd, ctx) => {
                var result = await activator.Deactivate(cmd, ctx.CancellationToken);
                ctx.Output(result);
            }
        );
    }
}

static partial class ConnectorsActivatorLogMessages {
    [LoggerMessage(
        Message = "[Node Id: {NodeId}] connector {ConnectorId} process started",
        Level = LogLevel.Debug
    )]
    internal static partial void LogProcessStarted(
        this ILogger logger, string nodeId, string connectorId
    );

    [LoggerMessage(
        Message = "[Node Id: {NodeId}] connector {ConnectorId} process stopped",
        Level = LogLevel.Debug
    )]
    internal static partial void LogProcessStopped(
        this ILogger logger, Exception? error, string nodeId, string connectorId
    );

    [LoggerMessage(
        Message = "[Node Id: {NodeId}] connector {ConnectorId} process failed",
        Level = LogLevel.Error
    )]
    internal static partial void LogProcessFailed(
        this ILogger logger, Exception? error, string nodeId, string connectorId
    );
}