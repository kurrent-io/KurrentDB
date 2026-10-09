// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Protocol.Registry.V2;

/// <summary>
/// Describes update schema request.
/// </summary>
public partial class UpdateSchemaRequest : IEntityCommand {
	string IEntityCommand.EntityId => SchemaName;
}
