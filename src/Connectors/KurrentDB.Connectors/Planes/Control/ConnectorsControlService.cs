// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.WellKnownTypes;
using KurrentDB.Connectors.Management.Contracts;
using KurrentDB.Connectors.Management.Contracts.Events;
using Kurrent.Surge;
using Kurrent.Surge.Connectors;

using KurrentDB.Connectors.Infrastructure.System.Node;
using KurrentDB.Connectors.Infrastructure.System.Node.NodeSystemInfo;
using KurrentDB.Connectors.Management.Contracts.Commands;
using KurrentDB.Connectors.Planes.Control.Model;
using KurrentDB.Connectors.Planes.Management;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Surge.Consumers;
using Microsoft.Extensions.Logging;
using ConnectorState = KurrentDB.Connectors.Management.Contracts.ConnectorState;

namespace KurrentDB.Connectors.Planes.Control;

public class ConnectorsControlService : LeaderNodeBackgroundService {
    public ConnectorsControlService(
        IPublisher publisher,
        ISubscriber subscriber,
        ISystemClient client,
        ConnectorsActivator activator,
        ConnectorsCommandApplication commandApplication,
        LoadActiveConnectorsSnapshot loadActiveConnectorsSnapshot,
        SaveActiveConnectorsSnapshot saveActiveConnectorsSnapshot,
        GetNodeSystemInfo getNodeSystemInfo,
        Func<SystemConsumerBuilder> getConsumerBuilder,
        TimeProvider time,
        ILoggerFactory loggerFactory,
        string? serviceName = null
    ) : base(publisher, subscriber, getNodeSystemInfo, loggerFactory, serviceName ?? "ConnectorsController") {
        Activator                    = activator;
        CommandApplication           = commandApplication;
        LoadActiveConnectorsSnapshot = loadActiveConnectorsSnapshot;
        SaveActiveConnectorsSnapshot = saveActiveConnectorsSnapshot;
        Time                         = time;

        ConsumerBuilder = getConsumerBuilder()
            .ConsumerId("ConnectorsController")
            .Client(client)
            .Filter(ConnectorsFeatureConventions.Filters.ManagementFilter)
            .InitialPosition(SubscriptionInitialPosition.Latest)
            .DisableAutoCommit();
    }

    static readonly TimeSpan SnapshotInterval = TimeSpan.FromMinutes(1);

    ConnectorsActivator          Activator                    { get; }
    LoadActiveConnectorsSnapshot LoadActiveConnectorsSnapshot { get; }
    SaveActiveConnectorsSnapshot SaveActiveConnectorsSnapshot { get; }
    ConnectorsCommandApplication CommandApplication           { get; }
    SystemConsumerBuilder        ConsumerBuilder              { get; }
    TimeProvider                 Time                         { get; }

    protected override async Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
        var connectors            = new ActiveConnectors();
        var caughtUp              = false;
        var lastSnapshotTimestamp = (long?)null;
        LogPosition lastSnapshotPosition;

        try {
            connectors           = await LoadActiveConnectorsSnapshot(stoppingToken);
            lastSnapshotPosition = connectors.Position.LogPosition;

            await using var consumer = ConsumerBuilder.StartPosition(connectors.Position).Create();

            await foreach (var record in consumer.Records(stoppingToken)) {
                connectors.Apply(record);

                switch (record.Value) {
                    case ReadResponse.CheckpointReceived:
                        await SaveSnapshot(connectors);
                        break;
                    case ReadResponse.SubscriptionCaughtUp when !caughtUp:
                        await connectors.Select(ActivateConnector).WhenAll();
                        caughtUp = true;
                        break;
                    case ConnectorActivating evt when caughtUp:
                        await ActivateConnector(connectors[evt.ConnectorId]);
                        break;
                    case ConnectorDeactivating evt when caughtUp:
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

        async ValueTask SaveSnapshot(ActiveConnectors active) {
            if (active.Position.LogPosition <= lastSnapshotPosition)
                return;

            if (lastSnapshotTimestamp is { } last && Time.GetElapsedTime(last) < SnapshotInterval)
                return;

            try {
                await SaveActiveConnectorsSnapshot(active);
                lastSnapshotPosition = active.Position.LogPosition;
            }
            catch (Exception ex) {
                Logger.LogSnapshotSaveFailure(ex, nodeInfo.InstanceId);
            }

            lastSnapshotTimestamp = Time.GetTimestamp();
        }

        async Task ActivateConnector(RegisteredConnector connector) {
            var connectorId      = connector.ConnectorId;
            var activationResult = await Activator.Activate(connectorId, connector.Settings, connector.Revision, stoppingToken);

            Logger.LogConnectorActivationResult(
                activationResult.Failure
                    ? activationResult.Type == ActivateResultType.RevisionAlreadyRunning ? LogLevel.Warning : LogLevel.Error
                    : LogLevel.Information,
                activationResult.Error, nodeInfo.InstanceId, connectorId, activationResult.Type
            );

            if (activationResult.Failure) {
                try {
                    await CommandApplication.Handle(
                        new RecordConnectorStateChange {
                            ConnectorId  = connectorId,
                            FromState    = ConnectorState.Activating,
                            ToState      = ConnectorState.Stopped,
                            ErrorDetails = activationResult.Error.MapErrorDetails(),
                            Timestamp    = TimeProvider.System.GetUtcNow().ToTimestamp()
                        },
                        stoppingToken
                    );
                }
                catch (Exception ex) {
                    Logger.LogActivationRecordFailure(ex, nodeInfo.InstanceId, connectorId);
                }
            }
        }

        async Task DeactivateConnector(ConnectorId connectorId) {
            var deactivationResult = await Activator.Deactivate(connectorId);

            Logger.LogConnectorDeactivationResult(
                deactivationResult.Failure
                    ? deactivationResult.Type == DeactivateResultType.UnableToReleaseLock ? LogLevel.Warning : LogLevel.Error
                    : LogLevel.Information,
                deactivationResult.Error, nodeInfo.InstanceId, connectorId, deactivationResult.Type
            );

            if (deactivationResult.Failure) {
                try {
                    await CommandApplication.Handle(
                        new RecordConnectorStateChange {
                            ConnectorId  = connectorId,
                            FromState    = ConnectorState.Deactivating,
                            ToState      = ConnectorState.Stopped,
                            ErrorDetails = deactivationResult.Error.MapErrorDetails(),
                            Timestamp    = TimeProvider.System.GetUtcNow().ToTimestamp()
                        },
                        stoppingToken
                    );
                }
                catch (Exception ex) {
                    Logger.LogDeactivationRecordFailure(ex, nodeInfo.InstanceId, connectorId);
                }
            }
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

    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "ConnectorsControlService [Node Id: {NodeId}] Failed to record connector {ConnectorId} activation failure",
        SkipEnabledCheck = true
    )]
    internal static partial void LogActivationRecordFailure(this ILogger logger, Exception error, Guid nodeId, string connectorId);

    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "ConnectorsControlService [Node Id: {NodeId}] Failed to record connector {ConnectorId} deactivation failure",
        SkipEnabledCheck = true
    )]
    internal static partial void LogDeactivationRecordFailure(this ILogger logger, Exception error, Guid nodeId, string connectorId);

    [LoggerMessage(LogLevel.Warning, "ConnectorsControlService [Node Id: {NodeId}] Failed to save activated connectors snapshot")]
    internal static partial void LogSnapshotSaveFailure(this ILogger logger, Exception error, Guid nodeId);
}
