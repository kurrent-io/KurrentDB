using EventStore.Connectors.Management.Contracts;
using Eventuous;
using FluentValidation.Results;

namespace EventStore.Connectors.Management;

public static class ConnectorDomainExceptions {
    public class ConnectorDeletedException(string connectorId) : DomainException($"Connector {connectorId} is deleted");

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