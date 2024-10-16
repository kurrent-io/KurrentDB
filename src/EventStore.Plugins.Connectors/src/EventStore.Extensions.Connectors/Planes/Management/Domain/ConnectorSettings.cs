using static EventStore.Connectors.Management.ConnectorDomainExceptions;
using static EventStore.Connectors.Management.ConnectorDomainServices;

namespace EventStore.Connectors.Management;

[PublicAPI]
public record ConnectorSettings(Dictionary<string, string?> Value) {
    public ConnectorSettings EnsureValid(string connectorId, ValidateConnectorSettings validate) {
        var validationResult = validate(Value);

        if (!validationResult.IsValid)
            throw new InvalidConnectorSettingsException(connectorId, validationResult.Errors);

        return this;
    }

    public IDictionary<string, string?> AsDictionary() => Value;

    public static ConnectorSettings From(IDictionary<string, string?> settings) => new(new Dictionary<string, string?>(settings));

    public static implicit operator Dictionary<string, string?>(ConnectorSettings settings) => settings.Value;
}