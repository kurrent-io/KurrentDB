using DotNext.Threading;
using EventStore.Connect.Connectors;
using EventStore.Connect.Consumers.Configuration;
using EventStore.Connectors.Control.Activation;
using EventStore.Connectors.Control.Coordination;
using EventStore.Connectors.System;
using EventStore.Streaming;
using Microsoft.Extensions.Logging;

using ManagementContracts = EventStore.Connectors.Management.Contracts.Events;

namespace EventStore.Connectors.Control;

public class ControlPlaneCoordinatorService : LeadershipAwareService {
    public ControlPlaneCoordinatorService(
        ConnectorsActivator activator, GetActiveConnectors getActiveConnectors, Func<SystemConsumerBuilder> getConsumerBuilder,
        GetNodeSystemInfo getNodeSystemInfo, INodeLifetimeService nodeLifetime, ILoggerFactory loggerFactory
    ) : base(nodeLifetime, getNodeSystemInfo, loggerFactory) {
        Activator           = activator;
        GetActiveConnectors = getActiveConnectors;

        ConsumerBuilder = getConsumerBuilder()
            .ConsumerId("conn-ctrl-coordinator-csx")
            .Filter(ConnectorsSystemConventions.Filters.ManagementFilter)
            .StartPosition(RecordPosition.Latest)
            .DisableAutoCommit();
    }

    ConnectorsActivator   Activator           { get; }
    GetActiveConnectors   GetActiveConnectors { get; }
    SystemConsumerBuilder ConsumerBuilder     { get; }

    protected override async Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
        GetConnectorsResult connectors = new();

        try {
            connectors = await GetActiveConnectors(stoppingToken);

            await connectors
                .Select(connector => ActivateConnector(connector.ConnectorId, connector.Settings, connector.Revision))
                .WhenAll();

            Logger.LogCoordinatorRunning(nodeInfo.InstanceId);

            await using var consumer = ConsumerBuilder.StartPosition(connectors.Position).Create();

            await foreach (var record in consumer.Records(stoppingToken)) {
                switch (record.Value) {
                    case ManagementContracts.ConnectorActivating activating:
                        await ActivateConnector(activating.ConnectorId, activating.Settings, activating.Revision);
                        break;

                    case ManagementContracts.ConnectorDeactivating deactivating:
                        await DeactivateConnector(deactivating.ConnectorId);
                        break;
                }
            }
        }
        catch (OperationCanceledException) {
            // ignore
        }
        finally {
            await connectors
                .Select(connector => DeactivateConnector(connector.ConnectorId))
                .WhenAll();

            Logger.LogCoordinatorStopped(nodeInfo.InstanceId);
        }

        return;

        async Task ActivateConnector(ConnectorId connectorId, IDictionary<string, string?> settings, int revision) {
            var activationResult = await Activator.Activate(connectorId, settings, revision, nodeInfo.InstanceId, stoppingToken);

            Logger.LogConnectorActivationResult(
                activationResult.Failure
                    ? activationResult.Type == ActivateResultType.RevisionAlreadyRunning ? LogLevel.Warning : LogLevel.Error
                    : LogLevel.Information,
                activationResult.Error, nodeInfo.InstanceId, connectorId, activationResult.Type
            );
        }

        async Task DeactivateConnector(ConnectorId connectorId) {
            var deactivationResult = await Activator.Deactivate(connectorId);

            Logger.LogConnectorDeactivationResult(
                deactivationResult.Failure
                    ? deactivationResult.Type == DeactivateResultType.UnableToReleaseLock ? LogLevel.Warning : LogLevel.Error
                    : LogLevel.Information,
                deactivationResult.Error, nodeInfo.InstanceId, connectorId, deactivationResult.Type
            );
        }
    }
}

static partial class ControlPlaneCoordinatorServiceLogMessages {
    [LoggerMessage(LogLevel.Information, "ConnectorsCoordinatorService [Node Id: {NodeId}] running on leader node")]
    internal static partial void LogCoordinatorRunning(this ILogger logger, Guid nodeId);

    [LoggerMessage(LogLevel.Information, "ConnectorsCoordinatorService [Node Id: {NodeId}] stopped because node lost leadership")]
    internal static partial void LogCoordinatorStopped(this ILogger logger, Guid nodeId);

    [LoggerMessage("ConnectorsCoordinatorService [Node Id: {NodeId}] connector {ConnectorId} {ResultType}")]
    internal static partial void LogConnectorActivationResult(
        this ILogger logger, LogLevel logLevel, Exception? error, Guid nodeId, string connectorId, ActivateResultType resultType
    );

    [LoggerMessage("ConnectorsCoordinatorService [Node Id: {NodeId}] connector {ConnectorId} {ResultType}")]
    internal static partial void LogConnectorDeactivationResult(
        this ILogger logger, LogLevel logLevel, Exception? error, Guid nodeId, string connectorId, DeactivateResultType resultType
    );
}