using FluentValidation.Results;
using static EventStore.Connectors.Management.ConnectorDomainExceptions;
using static EventStore.Connectors.Management.ConnectorDomainServices;

namespace EventStore.Connectors.Management;

[PublicAPI]
public record ConnectorSettings(IDictionary<string, string?> Settings, ValidationResult ValidationResult) {
    public bool IsValid => ValidationResult.IsValid;

    public ConnectorSettings EnsureValid(string connectorId) {
        if (!IsValid)
            throw new InvalidConnectorSettingsException(connectorId, ValidationResult.Errors);

        return this;
    }

    public static ConnectorSettings From(IDictionary<string, string?> settings, ValidateConnectorSettings validate) =>
        new(settings, validate(settings));

    public IDictionary<string, string?> ToDictionary() => Settings;
}