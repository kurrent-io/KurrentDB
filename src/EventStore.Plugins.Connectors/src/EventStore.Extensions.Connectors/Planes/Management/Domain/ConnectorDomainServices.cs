using FluentValidation.Results;

namespace EventStore.Connectors.Management;

public static class ConnectorDomainServices {
    public delegate ValidationResult ValidateConnectorSettings(IDictionary<string, string?> settings);

    public delegate ValueTask<IDictionary<string, string?>> ProtectConnectorSettings(string connectorId, IDictionary<string, string?> settings);
}