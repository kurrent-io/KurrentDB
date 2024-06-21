using System.Collections.Concurrent;
using DotNext.Collections.Generic;
using EventStore.Connect.Connectors;
using EventStore.Connectors.Control.Contracts;
using EventStore.Connectors.Control.Contracts.Activation;
using EventStore.Streaming;
using FluentValidation.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventStore.Connectors.Control.Activation;

// /// <summary>
// /// Activates or deactivates connectors
// /// </summary>
// public class ConnectorsActivator {
//     public ConnectorsActivator(ConnectorsTaskManager taskManager, TimeProvider? time = null, ILoggerFactory? loggerFactory = null) {
//         TaskManager = taskManager;
//         Time        = time ?? TimeProvider.System;
//         Logger      = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<ConnectorsActivator>();
//     }
//
//     ConnectorsTaskManager TaskManager { get; }
//     TimeProvider          Time        { get; }
//     ILogger               Logger      { get; }
//
//     public async Task<ConnectorsActivated> Activate(ActivateConnectors command, CancellationToken cancellationToken = default) {
//         var results = new List<ConnectorsTaskManager.ConnectorProcessInfo>();
//
//         foreach (var connector in command.Connectors) {
//             var result = await TaskManager.StartProcess(
//                 connector.ConnectorId,
//                 connector.Revision,
//                 new ConfigurationManager().AddInMemoryCollection(connector.Settings.ToDictionary()).Build(),
//                 cancellationToken
//             );
//
//             Logger.LogConnectorActivated(command.NodeId, connector.ConnectorId);
//
//             results.Add(result);
//         }
//
//         return new ConnectorsActivated {
//             ActivationId = command.ActivationId,
//             NodeId       = command.NodeId,
//             Connectors   = { results.MapToConnectors() },
//             ActivatedAt  = Time.MapToUtcNowTimestamp()
//         };
//     }
//
//     public async Task<ConnectorsDeactivated> Deactivate(DeactivateConnectors command, CancellationToken cancellationToken = default) {
//         var results = new List<ConnectorsTaskManager.ConnectorProcessInfo>();
//
//         foreach (var connector in command.Connectors) {
//             var result = await TaskManager.StopProcess(
//                 connector.ConnectorId,
//                 cancellationToken
//             );
//
//             Logger.LogConnectorDeactivated(result.Error, command.NodeId, connector.ConnectorId);
//
//             results.Add(result);
//         }
//
//         return new ConnectorsDeactivated {
//             ActivationId  = command.ActivationId,
//             NodeId        = command.NodeId,
//             Connectors    = { results.MapToConnectors() },
//             DeactivatedAt = Time.MapToUtcNowTimestamp()
//         };
//     }
//
//     // public async Task DeactivateAll() {
//     //     await TaskManager.StopAllProcesses();
//     // }
//
//     public async Task<List<Connector>> Connectors(CancellationToken cancellationToken = default) {
//         return TaskManager.GetProcesses()
//             .ConvertAll(process => new Connector {
//                 ConnectorId = process.ConnectorId,
//                 Revision    = process.Revision,
//                 // State       = process.State.MapToConnectorState(),
//                 // Error       = process.Error?.Message
//             });
//     }
// }

public record Lease(string Resource, string Owner, TimeSpan Duration, DateTimeOffset ExpiresAt, long Version);

public interface ILeaseManager {
    Task<Lease?> AcquireLease(string resource, string owner, TimeSpan duration, CancellationToken cancellationToken = default);
    Task<Lease?> AcquireOrRenewLease(string resource, string owner, TimeSpan duration, long? leaseVersion = null, CancellationToken cancellationToken = default);
    Task        ReleaseLease(string resource, string owner, long leaseVersion, CancellationToken cancellationToken = default);
}

public record ActivateResult {
    public record Activated : ActivateResult;

    public record InvalidConfiguration(ValidationResult ValidationResult) : ActivateResult;

    public record InstanceTypeNotFound(ConnectorTypeName InstanceType) : ActivateResult;

    public record LeaseAcquisitionFailed() : ActivateResult();

    public record ActivationError(Exception Exception) : ActivateResult;
}

public record DeactivateResult {
    public record Deactivated : DeactivateResult;

    public record ConnectorNotFound() : DeactivateResult;

    public record DeactivationError(Exception Exception) : DeactivateResult;
}

public class ConnectorsActivator {
    public ConnectorsActivator(ClusterNodeId nodeId, ILeaseManager leaseManager, IConnectorFactory connectorFactory, TimeProvider? time = null, ILoggerFactory? loggerFactory = null) {
        NodeId           = nodeId;
        LeaseManager     = leaseManager;
        ConnectorFactory = connectorFactory;
        LeasedConnectors = [];
        Time             = time ?? TimeProvider.System;
        Logger           = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<ConnectorsActivator>();

        LifetimeTokenSource = new CancellationTokenSource();
        LifetimeToken       = LifetimeTokenSource.Token;

        var leaseCheckInterval = TimeSpan.FromSeconds(5);

        Timer = new(leaseCheckInterval, OnLeaseCheck());

        Timer.Start(LifetimeToken);

        return;

        OnPeriodicTimerTick OnLeaseCheck() =>
            async (isLastTick, timerToken) => {
                await LeasedConnectors.Values.ForEachAsync(
                    async (leasedConnector, loopToken) => {
                        var lease = leasedConnector.Lease;

                        if (DateTimeOffset.UtcNow.AddMinutes(1) > lease.ExpiresAt) {
                            // renew lease
                            // if renew fails because it expired already we must disconnect the connector
                            // should we lock to avoid multiple renewals?

                            var newLease = await leaseManager
                                .AcquireOrRenewLease(
                                    lease.Resource,
                                    lease.Owner,
                                    lease.Duration,
                                    lease.Version,
                                    loopToken
                                );

                            if (newLease is null) {
                                await Deactivate(leasedConnector.Connector.ConnectorId);
                            }
                            else {
                                LeasedConnectors[leasedConnector.Connector.ConnectorId] = (
                                    leasedConnector.Connector,
                                    leasedConnector.Configuration,
                                    leasedConnector.Revision,
                                    newLease
                                );
                            }
                        }
                    },
                    timerToken
                );
            };
    }

    ClusterNodeId      NodeId           { get; }
    ILeaseManager      LeaseManager     { get; }
    IConnectorFactory  ConnectorFactory { get; }
    TimeProvider       Time             { get; }
    ILogger            Logger           { get; }
    AsyncPeriodicTimer Timer            { get; }

    CancellationTokenSource LifetimeTokenSource { get; }
    CancellationToken       LifetimeToken       { get; }

    ConcurrentDictionary<ConnectorId, (IConnector Connector, IConfiguration Configuration, int Revision, Lease Lease)> LeasedConnectors { get; }

    public async Task<ActivateResult> Activate(
        ConnectorId connectorId, int revision, Dictionary<string, string?> settings, CancellationToken stoppingToken = default
    ) {
        // link to lifetime token?

        // TODO SS: Seriously consider exposing the configuration settings on IConnector along with the ConnectorId and State
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var result = new ConnectorsValidation().ValidateConfiguration(config);

        if (!result.IsValid)
            return new ActivateResult.InvalidConfiguration(result);

        try {
            Lease? lease;

            if (LeasedConnectors.TryGetValue(connectorId, out var app)){
                lease = await LeaseManager.AcquireOrRenewLease(connectorId, NodeId, TimeSpan.FromMinutes(1), app.Lease.Version, stoppingToken);

                if (lease is null)
                    return new ActivateResult.LeaseAcquisitionFailed();

                // if deactivating we should wait, but without the stopped task or an event of sorts we cant...
                if (app.Revision != revision || app.Connector.State == ConnectorState.Stopped) {
                    await app.Connector.Disconnect();
                    LeasedConnectors.Remove(connectorId, out _);
                }
            }
            else {
                lease = await LeaseManager.AcquireLease(connectorId, NodeId, TimeSpan.FromMinutes(1), stoppingToken);

                if (lease is null)
                    return new ActivateResult.LeaseAcquisitionFailed();
            }

            var connector = ConnectorFactory.CreateConnector(connectorId, config);

            LeasedConnectors[connectorId] = (connector, config, revision, lease);

            await connector.Connect(stoppingToken);

            Logger.LogConnectorActivated(NodeId, connector.ConnectorId);

            return new ActivateResult.Activated();
        }
        catch (Exception ex) {
            return new ActivateResult.ActivationError(ex);
        }
    }

    public async Task<DeactivateResult> Deactivate(ConnectorId connectorId, CancellationToken cancellationToken = default) {
        // link to lifetime token?

        if (!LeasedConnectors.TryRemove(connectorId, out var app))
            return new DeactivateResult.ConnectorNotFound();

        // should disconnect receive a token that would work as a timeout?
        // it never fails but internally it captures errors
        await app.Connector.Disconnect();

        // try to release forever?
        // should return a result that we can check to see if it was successful
        await LeaseManager.ReleaseLease(connectorId, app.Lease.Owner, app.Lease.Version, cancellationToken);

        return new DeactivateResult.Deactivated();
    }
}

static partial class ConnectorsActivatorLogMessages {
    [LoggerMessage(
        Message = "[Node Id: {NodeId}] connector {ConnectorId} activated",
        Level = LogLevel.Debug
    )]
    internal static partial void LogConnectorActivated(
        this ILogger logger, string nodeId, string connectorId
    );

    [LoggerMessage(
        Message = "[Node Id: {NodeId}] connector {ConnectorId} deactivated",
        Level = LogLevel.Debug
    )]
    internal static partial void LogConnectorDeactivated(
        this ILogger logger, Exception? error, string nodeId, string connectorId
    );

    [LoggerMessage(
        Message = "[Node Id: {NodeId}] connector {ConnectorId} faulted",
        Level = LogLevel.Error
    )]
    internal static partial void LogConnectorFaulted(
        this ILogger logger, Exception? error, string nodeId, string connectorId
    );
}