using FluentValidation.Results;

namespace EventStore.Connectors.Management;

public static class ConnectorDomainServices {
    public delegate ValidationResult ValidateConnectorSettings(IDictionary<string, string?> settings);
}