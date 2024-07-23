using FluentValidation.Results;

namespace EventStore.Connectors.Management;

public static class ConnectorDomainServices {
    public delegate ValidationResult ValidateConnectorSettings(IDictionary<string, string?> settings);

    // public delegate void EnsureConnectorSettingsAreValid(IDictionary<string, string?> settings);
    //
    // var result = validateSettings(cmd.Settings.ToDictionary());
    //
    //     if (!result.IsValid)
    // throw new ConnectorDomainExceptions.InvalidConnectorSettings(cmd.ConnectorId, result);

}