// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;

namespace KurrentDB.SchemaRegistry.Domain;

/// <summary>
/// Delegate used to provide the current UTC date and time as a <see cref="DateTimeOffset"/>.
/// </summary>
/// <returns>The current UTC date and time represented as a <see cref="DateTimeOffset"/>.</returns>
/// <remarks>
/// This delegate allows for dependency injection of a time provider, enabling better testability
/// by potentially substituting a mock time source during unit testing.
/// </remarks>
public delegate DateTimeOffset GetUtcNow();

/// <summary>
/// Delegate representing an asynchronous method to check access rights
/// for a given gRPC server call context.
/// </summary>
/// <param name="context">The <see cref="ServerCallContext"/> representing the current gRPC call.</param>
/// <returns>A task that resolves to a boolean value indicating whether the access is allowed.</returns>
/// <remarks>
/// use KurrentDB for authentication
/// var authenticated = http.User.Identity?.IsAuthenticated ?? false;
/// </remarks>
public delegate ValueTask<bool> CheckAccess(ServerCallContext context);

/// <summary>
/// Delegate representing an asynchronous method to retrieve the schema name
/// for a given schema version identifier.
/// </summary>
/// <param name="schemaVersionId">The unique identifier of the schema version.</param>
/// <param name="cancellationToken">A token to cancel the ongoing operation if needed.</param>
/// <returns>A task that resolves to the schema name corresponding to the provided identifier.</returns>
/// <remarks>
/// This delegate is typically used in the context of actions related to schema management,
/// including retrieving schema names for existing versions when handling certain commands.
/// </remarks>
public delegate ValueTask<string> LookupSchemaNameByVersionId(string schemaVersionId, CancellationToken cancellationToken);