// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using FluentValidation.Results;

namespace KurrentDB.Connectors.Planes.Management.Domain;

/// <summary>
/// Provides a set of delegate definitions that represent key operations related to connector management
/// such as validation, configuration, deletion, and security of connector settings and streams.
/// </summary>
public static class ConnectorDomainServices {
    /// <summary>
    /// Represents a delegate responsible for securing and modifying connector settings
    /// for a specified connector, allowing customization or encryption of the settings
    /// as needed.
    /// </summary>
    public delegate IDictionary<string, string?> ProtectConnectorSettings(string connectorId, IDictionary<string, string?> settings);

    /// <summary>
    /// Represents a delegate that defines the operation for configuring the streams associated
    /// with a connector, based on its unique identifier.
    /// </summary>
    public delegate bool ConfigureConnectorStreams(string connectorId);

    /// <summary>
    /// Represents a delegate responsible for defining the operation to delete
    /// all streams associated with a specified connector, identified by its unique identifier.
    /// </summary>
    public delegate bool DeleteConnectorStreams(string connectorId);

    /// <summary>
    /// Represents a delegate for validating a set of connector settings.
    /// Returns a ValidationResult indicating whether the settings meet the required criteria.
    /// </summary>
    public delegate ValidationResult ValidateConnectorSettings(IDictionary<string, string?> settings);
}