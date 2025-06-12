// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Connectors.Management.Contracts;
using KurrentDB.Connectors.Management.Contracts.Events;
using Kurrent.Surge;
using Kurrent.Surge.Connectors;

using KurrentDB.Connectors.Infrastructure.System.Node;
using KurrentDB.Connectors.Infrastructure.System.Node.NodeSystemInfo;
using KurrentDB.Core.Bus;
using KurrentDB.Surge.Consumers;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Connectors.Planes.Control;

public class ConnectorsControlService : LeaderNodeBackgroundService {
    public ConnectorsControlService(
        IPublisher publisher,
        ISubscriber subscriber,
        ConnectorsActivator activator,
        GetActiveConnectors getActiveConnectors,
        GetNodeSystemInfo getNodeSystemInfo,
        Func<SystemConsumerBuilder> getConsumerBuilder,
        ILoggerFactory loggerFactory
    ) : base(publisher, subscriber, getNodeSystemInfo, loggerFactory, "ConnectorsController") {
        Activator           = activator;
        GetActiveConnectors = getActiveConnectors;

        ConsumerBuilder = getConsumerBuilder()
            .ConsumerId("ConnectorsController")
            .Publisher(publisher)
            .Filter(ConnectorsFeatureConventions.Filters.ManagementFilter)
            .InitialPosition(SubscriptionInitialPosition.Latest)
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

            await using var consumer = ConsumerBuilder.StartPosition(connectors.Position).Create();

            await foreach (var record in consumer.Records(stoppingToken)) {
                switch (record.Value) {
                    case ConnectorActivating evt:
                        var connector = new RegisteredConnector(evt.ConnectorId, evt.Revision, EnrichWithStartPosition(evt.Settings, evt.StartFrom));
                        connectors.Connectors.Add(connector);
                        await ActivateConnector(connector.ConnectorId, connector.Settings, connector.Revision);
                        break;
                    case ConnectorDeactivating evt:
                        connectors.Connectors.RemoveAll(x => x.ConnectorId == evt.ConnectorId);
                        await DeactivateConnector(evt.ConnectorId);
                        break;
                }
            }
        }
        catch (OperationCanceledException) {
            // ignore
        }
        finally {
            // // this exists to effectively wait for all connectors to be deactivated...
            // await connectors
            //     .Select(connector => DeactivateConnector(connector.ConnectorId))
            //     .WhenAll();

            // this exists to effectively wait for all connectors to be deactivated...
            await connectors
                .Select(connector => Activator.WaitForDeactivation(connector.ConnectorId))
                .WhenAll();
        }

        return;

        static IDictionary<string, string?> EnrichWithStartPosition(IDictionary<string, string?> settings, StartFromPosition? startPosition) {
            if (startPosition is not null)
                settings["Subscription:StartPosition"] = startPosition.LogPosition.ToString();

            return settings;
        }

        async Task ActivateConnector(ConnectorId connectorId, IDictionary<string, string?> settings, int revision) {
            var activationResult = await Activator.Activate(connectorId, settings, revision, stoppingToken);

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
    [LoggerMessage("ConnectorsControlService [Node Id: {NodeId}] connector {ConnectorId} {ResultType}")]
    internal static partial void LogConnectorActivationResult(
        this ILogger logger, LogLevel logLevel, Exception? error, Guid nodeId, string connectorId, ActivateResultType resultType
    );

    [LoggerMessage("ConnectorsControlService [Node Id: {NodeId}] connector {ConnectorId} {ResultType}")]
    internal static partial void LogConnectorDeactivationResult(
        this ILogger logger, LogLevel logLevel, Exception? error, Guid nodeId, string connectorId, DeactivateResultType resultType
    );
}
