// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using FluentValidation.Results;

namespace EventStore.Connectors.Management;

public static class ConnectorDomainServices {
    public delegate IDictionary<string, string?> ProtectConnectorSettings(string connectorId, IDictionary<string, string?> settings);

    public delegate bool TryConfigureStream(string connectorId);

    public delegate ValidationResult ValidateConnectorSettings(IDictionary<string, string?> settings);
}