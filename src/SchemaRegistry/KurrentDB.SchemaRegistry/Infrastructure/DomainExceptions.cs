// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using Humanizer;

namespace KurrentDB.SchemaRegistry.Services.Domain;

[PublicAPI]
public class DomainExceptions {
    public class EntityNotFound(string entityType, string entityId)
        : EntityException($"{entityType} {entityId} not found");

    public class EntityDeleted(string entityType, string entityId, DateTimeOffset timestamp)
        : EntityException($"{entityType} {entityId} deleted {timestamp.Humanize()}");

    public class EntityAlreadyExists(string entityType, string entityId)
        : EntityException($"{entityType} {entityId} already exists");

    public class EntityNotModified(string entityType, string entityId, string message)
        : EntityException($"{entityType} {entityId} not modified: {message}");

    public class InvalidEntityStatus(string entityType, string entityId, string status)
        : EntityException($"{entityType} {entityId} status is {status}");

    public class EntityException(string message) : DomainException(message);
}