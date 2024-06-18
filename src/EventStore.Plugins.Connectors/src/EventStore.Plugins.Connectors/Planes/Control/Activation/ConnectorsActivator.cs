using EventStore.Connectors.Control.Contracts;
using EventStore.Connectors.Control.Contracts.Activation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventStore.Connectors.Control.Activation;

/// <summary>
/// Activates or deactivates connectors
/// </summary>
public class ConnectorsActivator {
    public ConnectorsActivator(ConnectorsTaskManager taskManager, TimeProvider? time = null, ILoggerFactory? loggerFactory = null) {
        TaskManager = taskManager;
        Time        = time ?? TimeProvider.System;
        Logger      = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<ConnectorsActivator>();
    }

    ConnectorsTaskManager TaskManager { get; }
    TimeProvider          Time        { get; }
    ILogger               Logger      { get; }

    public async Task<ConnectorsActivated> Activate(ActivateConnectors command, CancellationToken cancellationToken = default) {
        var results = new List<ConnectorsTaskManager.ConnectorProcessInfo>();

        foreach (var connector in command.Connectors) {
            var result = await TaskManager.StartProcess(
                connector.ConnectorId,
                connector.Revision,
                new(connector.Settings.ToDictionary()),
                cancellationToken
            );

            Logger.LogProcessStarted(command.NodeId, connector.ConnectorId);

            results.Add(result);
        }

        return new ConnectorsActivated {
            ActivationId = command.ActivationId,
            NodeId       = command.NodeId,
            Connectors   = { results.MapToConnectors() },
            ActivatedAt  = Time.MapToUtcNowTimestamp()
        };
    }

    public async Task<ConnectorsDeactivated> Deactivate(DeactivateConnectors command, CancellationToken cancellationToken = default) {
        var results = new List<ConnectorsTaskManager.ConnectorProcessInfo>();

        foreach (var connector in command.Connectors) {
            var result = await TaskManager.StopProcess(
                connector.ConnectorId,
                cancellationToken
            );

            Logger.LogProcessStopped(result.Error, command.NodeId, connector.ConnectorId);

            results.Add(result);
        }

        return new ConnectorsDeactivated {
            ActivationId  = command.ActivationId,
            NodeId        = command.NodeId,
            Connectors    = { results.MapToConnectors() },
            DeactivatedAt = Time.MapToUtcNowTimestamp()
        };
    }

    public async Task DeactivateAll() {
        await TaskManager.StopAllProcesses();
    }

    public async Task<List<Connector>> Connectors(CancellationToken cancellationToken = default) {
        return TaskManager.GetProcesses()
            .ConvertAll(process => new Connector {
                ConnectorId = process.ConnectorId,
                Revision    = process.Revision,
                // State       = process.State.MapToConnectorState(),
                // Error       = process.Error?.Message
            });
    }
}