// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Connectors;
using Microsoft.Extensions.Configuration;

namespace KurrentDB.Connectors.Infrastructure.Connect.Components.Connectors;

public interface ISystemConnectorFactory {
    IConnector CreateConnector(ConnectorId connectorId, IConfiguration configuration);
}