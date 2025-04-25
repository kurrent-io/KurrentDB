// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using Kurrent.Surge.Connectors;
using Microsoft.Extensions.Configuration;

namespace KurrentDB.Connectors.Infrastructure.Connect.Components.Connectors;

public interface ISystemConnectorFactory {
    IConnector CreateConnector(ConnectorId connectorId, IConfiguration configuration);
}