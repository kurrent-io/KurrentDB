using EventStore.Connect.Connectors;
using EventStore.Connect.Consumers.Configuration;
using EventStore.Connectors.Management.Contracts.Events;
using EventStore.Connectors.System;
using EventStore.Streaming;
using Microsoft.Extensions.Logging;

namespace EventStore.Connectors.Control;

public class ConnectorsControlService : LeadershipAwareService {
    public ConnectorsControlService(
        ConnectorsActivator activator, GetActiveConnectors getActiveConnectors, Func<SystemConsumerBuilder> getConsumerBuilder,
        GetNodeSystemInfo getNodeSystemInfo, INodeLifetimeService nodeLifetime, ILoggerFactory loggerFactory
    ) : base(nodeLifetime, getNodeSystemInfo, loggerFactory) {
        Activator           = activator;
        GetActiveConnectors = getActiveConnectors;

        ConsumerBuilder = getConsumerBuilder()
            .ConsumerId("conn-ctrl-coordinator-csx")
            .Filter(ConnectorsFeatureConventions.Filters.ManagementFilter)
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

            Logger.LogControlServiceRunning(nodeInfo.InstanceId);

            await using var consumer = ConsumerBuilder.StartPosition(connectors.Position).Create();

            await foreach (var record in consumer.Records(stoppingToken)) {
                await (record.Value switch {
                    ConnectorActivating   evt => ActivateConnector(evt.ConnectorId, evt.Settings, evt.Revision),
                    ConnectorDeactivating evt => DeactivateConnector(evt.ConnectorId),
                    _                         => Task.CompletedTask
                });
            }
        }
        catch (OperationCanceledException) {
            // ignore
        }
        finally {
            await connectors
                .Select(connector => DeactivateConnector(connector.ConnectorId))
                .WhenAll();

            Logger.LogControlServiceStopped(nodeInfo.InstanceId);
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

static partial class ConnectorsControlServiceLogMessages {
    [LoggerMessage(LogLevel.Information, "[Node Id: {NodeId}] running on leader node")]
    internal static partial void LogControlServiceRunning(this ILogger logger, Guid nodeId);

    [LoggerMessage(LogLevel.Information, "[Node Id: {NodeId}] stopped because node lost leadership")]
    internal static partial void LogControlServiceStopped(this ILogger logger, Guid nodeId);

    [LoggerMessage("[Node Id: {NodeId}] connector {ConnectorId} {ResultType}")]
    internal static partial void LogConnectorActivationResult(
        this ILogger logger, LogLevel logLevel, Exception? error, Guid nodeId, string connectorId, ActivateResultType resultType
    );

    [LoggerMessage("[Node Id: {NodeId}] connector {ConnectorId} {ResultType}")]
    internal static partial void LogConnectorDeactivationResult(
        this ILogger logger, LogLevel logLevel, Exception? error, Guid nodeId, string connectorId, DeactivateResultType resultType
    );
}