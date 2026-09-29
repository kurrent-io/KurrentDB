// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Protocol.Registry.V2;

/// <summary>
/// Describes schema registration request.
/// </summary>
public partial class RegisterSchemaVersionRequest : IEntityCommand {
	string IEntityCommand.EntityId => SchemaName;
}
