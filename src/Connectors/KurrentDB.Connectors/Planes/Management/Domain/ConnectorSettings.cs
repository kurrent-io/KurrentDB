// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using static System.StringComparer;
using static KurrentDB.Connectors.Planes.Management.Domain.ConnectorDomainExceptions;
using static KurrentDB.Connectors.Planes.Management.Domain.ConnectorDomainServices;

namespace KurrentDB.Connectors.Planes.Management.Domain;

[PublicAPI]
public record ConnectorSettings(Dictionary<string, string?> Value, string ConnectorId) {
    public ConnectorSettings EnsureValid(ValidateConnectorSettings validate) {
        var validationResult = validate(Value);

        if (!validationResult.IsValid)
            throw new InvalidConnectorSettingsException(ConnectorId, validationResult.Errors);

        return this;
    }

    public ConnectorSettings Protect(ProtectConnectorSettings protect) =>
        From(protect(ConnectorId, Value), ConnectorId);

    public IDictionary<string, string?> AsDictionary() => Value;

    public static ConnectorSettings From(IDictionary<string, string?> settings, string connectorId) =>
        new(new(settings, OrdinalIgnoreCase), connectorId);

    public static implicit operator Dictionary<string, string?>(ConnectorSettings settings) =>
        settings.Value;
}