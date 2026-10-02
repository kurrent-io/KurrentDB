// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Connectors.Management.Contracts.Commands;

/// <summary>
/// Describes reset connector request.
/// </summary>
partial class ResetConnector : ICommand {
	string ICommand.EntityId => ConnectorId;
}
