using EventStore.Connect.Connectors;
using FluentValidation;
using ActivatedConnectors = System.Collections.Concurrent.ConcurrentDictionary<
    EventStore.Connect.Connectors.ConnectorId,
    (EventStore.Connect.Connectors.IConnector Instance, int Revision)
>;

namespace EventStore.Connectors.Control;

public delegate IConnector CreateConnector(ConnectorId connectorId, IDictionary<string, string?> settings);

public class ConnectorsActivator(CreateConnector createConnector) {
    public ConnectorsActivator(IConnectorFactory connectorFactory) : this(connectorFactory.CreateConnector) { }

    CreateConnector     CreateConnector { get; } = createConnector;
    ActivatedConnectors Connectors      { get; } = [];

    public async Task<ActivateResult> Activate(
        ConnectorId connectorId,
        IDictionary<string, string?> settings,
        int revision,
        CancellationToken stoppingToken = default
    ) {
        // at this moment, this should not happen and its more of a sanity check
        if (Connectors.TryGetValue(connectorId, out var connector)) {
            if (connector.Revision == revision && connector.Instance.State is ConnectorState.Activating or ConnectorState.Running)
                return ActivateResult.RevisionAlreadyRunning();

            try {
                await connector.Instance.DisposeAsync();
                await connector.Instance.Stopped;
            }
            catch {
                // ignore
            }
        }

        try {
            connector = Connectors[connectorId] = (CreateConnector(connectorId, settings), revision);

            await connector.Instance.Connect(stoppingToken);

            // For debugging purposes
            // _ = connector.Instance.Stopped
            //     .OnError(ex => Console.Error.Write($"Connector Failure Detected! {(ex is AggregateException ae ? ae.Flatten() : ex)}"));

            return ActivateResult.Activated();
        }
        catch (ValidationException ex) {
            return ActivateResult.InvalidConfiguration(ex);
        }
        catch (Exception ex) {
            return ActivateResult.UnknownError(ex);
        }
    }

    public async Task<DeactivateResult> Deactivate(ConnectorId connectorId) {
        if (!Connectors.TryRemove(connectorId, out var connector))
            return DeactivateResult.ConnectorNotFound();

        try {
            await connector.Instance.DisposeAsync();
            await connector.Instance.Stopped;

            return DeactivateResult.Deactivated();
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
    UnableToAcquireLock    = 5
}

public record ActivateResult(ActivateResultType Type, Exception? Error = null) {
    public bool Success => Type is ActivateResultType.Activated;
    public bool Failure => !Success;

    public static ActivateResult Activated() => new(ActivateResultType.Activated);

    public static ActivateResult RevisionAlreadyRunning() => new(ActivateResultType.RevisionAlreadyRunning);

    public static ActivateResult InstanceTypeNotFound() => new(ActivateResultType.InstanceTypeNotFound);

    public static ActivateResult InvalidConfiguration(ValidationException error) => new(ActivateResultType.InvalidConfiguration, error);

    public static ActivateResult UnableToAcquireLock() => new(ActivateResultType.UnableToAcquireLock);

    public static ActivateResult UnknownError(Exception error) => new(ActivateResultType.Unknown, error);
}

public enum DeactivateResultType {
    Unknown              = 0,
    Deactivated          = 1,
    ConnectorNotFound    = 2,
    UnableToReleaseLock  = 3
}

public record DeactivateResult(DeactivateResultType Type, Exception? Error = null) {
    public bool Success => Type is DeactivateResultType.Deactivated;
    public bool Failure => !Success;

    public static DeactivateResult Deactivated() => new(DeactivateResultType.Deactivated);

    public static DeactivateResult ConnectorNotFound() => new(DeactivateResultType.ConnectorNotFound);

    public static DeactivateResult UnableToReleaseLock() => new(DeactivateResultType.UnableToReleaseLock);

    public static DeactivateResult UnknownError(Exception error) => new(DeactivateResultType.Unknown, error);
}

// [PublicAPI]
// public interface IActivationResult {
//     public bool Success => this is Activated;
//     public bool Failure => !Success;
//
//     public Exception? Error { get; init; }
//
//     public readonly record struct Activated : IActivationResult {
//         public Exception? Error { get; init; }
//     }
//
//     public readonly record struct RevisionAlreadyRunning : IActivationResult {
//         public Exception? Error { get; init; }
//     }
//
//     public readonly record struct InstanceTypeNotFound : IActivationResult {
//         public Exception? Error { get; init; }
//     }
//
//     public readonly record struct InvalidConfiguration : IActivationResult {
//         public Exception? Error { get; init; }
//     }
//
//     public readonly record struct UnableToAcquireLock : IActivationResult {
//         public Exception? Error { get; init; }
//     }
//
//     public readonly record struct UnknownError : IActivationResult {
//         public Exception? Error { get; init; }
//     }
//
//     // public static readonly Activated              ActivatedResult              = new Activated();
//     // public static readonly RevisionAlreadyRunning RevisionAlreadyRunningResult = new RevisionAlreadyRunning();
//     // public static readonly InstanceTypeNotFound   InstanceTypeNotFoundResult   = new InstanceTypeNotFound();
//     // public static readonly InvalidConfiguration   InvalidConfigurationResult   = new InvalidConfiguration();
//     // public static readonly UnableToAcquireLock    UnableToAcquireLockResult    = new UnableToAcquireLock();
//     // public static readonly UnknownError           UnknownErrorResult           = new UnknownError();
// }

// [PublicAPI]
// public interface IActivationResult {
//     public bool       Success { get; init; }
//     public Exception? Error   { get; init; }
//
//     public readonly record struct Activated() : IActivationResult {
//         public bool       Success { get; init; } = true;
//         public Exception? Error   { get; init; } = null;
//     }
//
//     public readonly record struct RevisionAlreadyRunning() : IActivationResult {
//         public bool       Success { get; init; } = false;
//         public Exception? Error   { get; init; } = null;
//     }
//
//     public readonly record struct InstanceTypeNotFound() : IActivationResult {
//         public bool       Success { get; init; } = false;
//         public Exception? Error   { get; init; } = null;
//     }
//
//     public readonly record struct InvalidConfiguration() : IActivationResult {
//         public bool       Success { get; init; } = false;
//         public Exception? Error   { get; init; } = null;
//     }
//
//     public readonly record struct UnableToAcquireLock() : IActivationResult {
//         public bool       Success { get; init; } = false;
//         public Exception? Error   { get; init; } = null;
//     }
//
//     public readonly record struct UnknownError() : IActivationResult {
//         public bool       Success { get; init; } = false;
//         public Exception? Error   { get; init; } = null;
//     }
//
//     // public static readonly Activated              ActivatedResult              = new Activated();
//     // public static readonly RevisionAlreadyRunning RevisionAlreadyRunningResult = new RevisionAlreadyRunning();
//     // public static readonly InstanceTypeNotFound   InstanceTypeNotFoundResult   = new InstanceTypeNotFound();
//     // public static readonly InvalidConfiguration   InvalidConfigurationResult   = new InvalidConfiguration();
//     // public static readonly UnableToAcquireLock    UnableToAcquireLockResult    = new UnableToAcquireLock();
//     // public static readonly UnknownError           UnknownErrorResult           = new UnknownError();
// }
//
// public static class ActivationResults {
//     public static readonly IActivationResult.Activated              Activated              = new();
//     public static readonly IActivationResult.RevisionAlreadyRunning RevisionAlreadyRunning = new();
//     public static readonly IActivationResult.InstanceTypeNotFound   InstanceTypeNotFound   = new();
//     public static readonly IActivationResult.InvalidConfiguration   InvalidConfiguration   = new();
//     public static readonly IActivationResult.UnableToAcquireLock    UnableToAcquireLock    = new();
//     public static readonly IActivationResult.UnknownError           UnknownError           = new();
// }



/////////////////
/// ////////////
/// ////////////
///
/// <summary>
///  working
/// </summary>
//
// [PublicAPI]
// public interface IActivationResult {
//     public bool       Success { get; }
//     public Exception? Error   { get; }
// }
//
// [PublicAPI]
// public abstract record ActivationResult(bool Success = false, Exception? Error = null) : IActivationResult {
//     public record Activated() : ActivationResult(true);
//
//     public record RevisionAlreadyRunning : ActivationResult;
//
//     public record InstanceTypeNotFound : ActivationResult;
//
//     public record InvalidConfiguration : ActivationResult;
//
//     public record UnableToAcquireLock : ActivationResult;
//
//     public record UnknownError : ActivationResult;
// }
//
// public static class ActivationResults {
//     static readonly ActivationResult.Activated              StaticActivated              = new();
//     static readonly ActivationResult.RevisionAlreadyRunning StaticRevisionAlreadyRunning = new();
//     static readonly ActivationResult.InstanceTypeNotFound   StaticInstanceTypeNotFound   = new();
//
//     public static ActivationResult.Activated              Activated()              => StaticActivated;
//     public static ActivationResult.RevisionAlreadyRunning RevisionAlreadyRunning() => StaticRevisionAlreadyRunning;
//     public static ActivationResult.InstanceTypeNotFound   InstanceTypeNotFound()   => StaticInstanceTypeNotFound;
//
//     public static ActivationResult.InvalidConfiguration InvalidConfiguration(ValidationException error) => new() {
//         Success = false,
//         Error   = error
//     };
//
//     public static ActivationResult.UnableToAcquireLock UnableToAcquireLock(Exception? error = null) => new() {
//         Success = false,
//         Error   = error
//     };
//
//     public static ActivationResult.UnknownError UnknownError(Exception? error = null) => new() {
//         Success = false,
//         Error   = error
//     };
// }




[PublicAPI]
public interface IActivationResult {
    public bool       Success { get; }
    public Exception? Error   { get; }

    public readonly record struct Activated : IActivationResult {
        public Exception? Error   => null;
        public bool       Success => true;
    }

    public readonly record struct RevisionAlreadyRunning : IActivationResult{
        internal RevisionAlreadyRunning(Exception error) {
            Error = error;
            Success = false;
        }

        public Exception? Error   { get; }
        public bool       Success { get; }
    }

    public readonly record struct InvalidConfiguration : IActivationResult{
        internal InvalidConfiguration(ValidationException error) {
            Error   = error;
            Success = false;
        }

        public Exception? Error   { get; }
        public bool       Success { get; }
    }

    public readonly record struct UnknownError : IActivationResult{
        internal UnknownError(Exception error) {
            Error = error;
            Success = false;
        }

        public Exception? Error   { get; }
        public bool       Success { get; }
    }
}

public static class ActivationResults {
    static readonly IActivationResult.Activated              StaticActivated              = new();
    static readonly IActivationResult.RevisionAlreadyRunning StaticRevisionAlreadyRunning = new();

    public static IActivationResult.Activated              Activated()              => StaticActivated;
    public static IActivationResult.RevisionAlreadyRunning RevisionAlreadyRunning() => StaticRevisionAlreadyRunning;

    public static IActivationResult.InvalidConfiguration InvalidConfiguration(ValidationException error) => new(error);

    public static IActivationResult.UnknownError UnknownError(Exception error) => new(error);
}