// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Connectors.Management.Contracts.Commands;

/// <summary>
/// Represents a root for command pattern.
/// </summary>
public interface ICommand {
	/// <summary>
	/// Gets the identifier of the entity encapsulated by this command.
	/// </summary>
	string EntityId { get; }
}
