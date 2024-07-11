using System.Collections.Concurrent;
using EventStore.Connect.Connectors;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventStore.Connectors.Control.Activation;

public class ConnectorsActivator {
    public ConnectorsActivator(IConnectorFactory connectorFactory, ILogger<ConnectorsActivator>? logger = null) {
        ConnectorFactory = connectorFactory;
        Logger           = logger ?? NullLoggerFactory.Instance.CreateLogger<ConnectorsActivator>();

        Connectors = [];
    }

    IConnectorFactory ConnectorFactory { get; }
    ILogger           Logger           { get; }

    ConcurrentDictionary<ConnectorId, (IConnector Instance, int Revision)> Connectors { get; }

    public async Task<ActivateResult> Activate(
        ConnectorId connectorId, Dictionary<string, string?> settings, int revision, ClusterNodeId ownerId, CancellationToken stoppingToken = default
    ) {
        if (stoppingToken.IsCancellationRequested)
            return ActivateResult.OperationCanceled();

        // TODO SS: the factory must validate the connector configuration
        // var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        // var result = new ConnectorsValidation().ValidateConfiguration(config);
        // if (!result.IsValid) return new ActivateResult.InvalidConfiguration(result);

        // this should not happen and its more of a sanity check
        if (Connectors.TryGetValue(connectorId, out var connector)) {
            if (connector.Revision == revision && connector.Instance.State is ConnectorState.Activating or ConnectorState.Running)
                return ActivateResult.RevisionAlreadyRunning();

            try {
                await connector.Instance.DisposeAsync();
            }
            catch {
                // ignore
            }
        }

        try {
            //TODO SS: the factory must be refactored to accept a owner id to be used in lock acquisition
            connector = Connectors[connectorId] = (ConnectorFactory.CreateConnector(connectorId, settings), revision);

            if (stoppingToken.IsCancellationRequested)
                return ActivateResult.OperationCanceled();

            await connector.Instance.Connect(stoppingToken);

            Logger.LogConnectorActivated(ownerId, connector.Instance.ConnectorId);

            return ActivateResult.Activated();
        }
        catch (ValidationException ex) {
            return ActivateResult.InvalidConfiguration(ex);
        }
        catch (Exception ex) {
            return ActivateResult.UnknownError(ex);
        }
    }

    public async Task<DeactivateResult> Deactivate(ConnectorId connectorId, CancellationToken cancellationToken = default) {
        if (!Connectors.TryRemove(connectorId, out var app))
            return DeactivateResult.ConnectorNotFound();

        try {
            // should disconnect receive a token that would work as a timeout?
            // it never fails but internally it captures errors
            await app.Instance.DisposeAsync(); //Disconnect();

            return DeactivateResult.Deactivated();
        }
        catch (OperationCanceledException) {
            return DeactivateResult.OperationCanceled();
        }
        catch (Exception ex) {
            return DeactivateResult.UnknownError(ex);
        }
    }
}

public enum ActivateResultType {
    Unknown                = 0,
    Activated              = 1,
    RevisionAlreadyRunning = 2,
    InstanceTypeNotFound   = 3,
    InvalidConfiguration   = 4,
    UnableToAcquireLock    = 5,
    OperationCanceled      = 6
}

public record ActivateResult(ActivateResultType Type, Exception? Error = null) {
    public bool Success => Type is ActivateResultType.Activated;
    public bool Failure => !Success;

    public static ActivateResult Activated() => new(ActivateResultType.Activated);

    public static ActivateResult RevisionAlreadyRunning() => new(ActivateResultType.RevisionAlreadyRunning);

    public static ActivateResult InstanceTypeNotFound() => new(ActivateResultType.InstanceTypeNotFound);

    public static ActivateResult InvalidConfiguration(ValidationException error) => new(ActivateResultType.InvalidConfiguration, error);

    public static ActivateResult UnableToAcquireLock() => new(ActivateResultType.UnableToAcquireLock);

    public static ActivateResult OperationCanceled() => new(ActivateResultType.OperationCanceled);

    public static ActivateResult UnknownError(Exception error) => new(ActivateResultType.Unknown, error);
}

public enum DeactivateResultType {
    Unknown              = 0,
    Deactivated          = 1,
    ConnectorNotFound    = 2,
    UnableToReleaseLock  = 3,
    OperationCanceled    = 4
}

public record DeactivateResult(DeactivateResultType Type, Exception? Error = null) {
    public bool Success => Type is DeactivateResultType.Deactivated;
    public bool Failure => !Success;

    public static DeactivateResult Deactivated() => new(DeactivateResultType.Deactivated);

    public static DeactivateResult ConnectorNotFound() => new(DeactivateResultType.ConnectorNotFound);

    public static DeactivateResult UnableToReleaseLock() => new(DeactivateResultType.UnableToReleaseLock);

    public static DeactivateResult OperationCanceled() => new(DeactivateResultType.OperationCanceled);

    public static DeactivateResult UnknownError(Exception error) => new(DeactivateResultType.Unknown, error);
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