using EventStore.Connectors.Management.Contracts;
using Eventuous;
using FluentValidation.Results;
using Humanizer;

namespace EventStore.Connectors.Management;

[PublicAPI]
public static class DomainExceptions {
    public class EntityNotFound : EntityException {
        public EntityNotFound(string entityType, string uid)
            : base($"{entityType} {uid} not found") { }
    }

    public class EntityDeleted : EntityException {
        public EntityDeleted(string entityType, string uid, DateTimeOffset timestamp)
            : base($"{entityType} {uid} deleted on {timestamp.Humanize()}") { }
    }

    public class EntityAlreadyExists : EntityException {
        public EntityAlreadyExists(string entityType, string uid)
            : base($"{entityType} {uid} already exists") { }
    }

    public class EntityNotModified : EntityException {
        public EntityNotModified(string entityType, string uid, string message)
            : base($"{entityType} {uid} not modified: {message}") { }
    }

    public class InvalidEntityStatus : EntityException {
        public InvalidEntityStatus(string entityType, string uid, string status)
            : base($"{entityType} {uid} status is {status}") { }
    }

    public class EntityException : DomainException {
        public EntityException(string message)
            : base(message) { }
    }
}

public static class ConnectorDomainExceptions {
    public class ConnectorDeletedException(string connectorId) : DomainException($"Connector {connectorId} deleted");

    public class OldLogPositionException(string connectorId, ulong newPosition, ulong actualPosition)
        : DomainException($"Connector {connectorId} new log position {newPosition} is older than the actual {actualPosition}");

    public class InvalidConnectorStateChangeException(string connectorId, ConnectorState currentState, ConnectorState requestedState)
        : DomainException($"Connector {connectorId} invalid state change from {currentState} to {requestedState} detected");

    public class InvalidConnectorSettingsException(string connectorId, Dictionary<string, string[]> errors)
        : DomainException($"Connector {connectorId} invalid settings detected") {
        public IDictionary<string, string[]> Errors { get; } = errors;

        public InvalidConnectorSettingsException(string connectorId, List<ValidationFailure> failures)
            : this(connectorId, failures.GroupBy(x => x.PropertyName).ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray())) { }
    }
}