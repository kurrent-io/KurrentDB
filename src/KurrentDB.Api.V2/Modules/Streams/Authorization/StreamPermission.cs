// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Authorization;
using KurrentDB.Api.Infrastructure.Authorization;

using static EventStore.Plugins.Authorization.Operations.Streams.Parameters;

namespace KurrentDB.Api.Streams.Authorization;

/// <summary>
/// Represents operations that can be performed on streams for authorization purposes.
/// </summary>
[PublicAPI]
public sealed record StreamPermission : Permission {
    public static readonly StreamPermission Read          = Create<StreamPermission>("streams:read", Operations.Streams.Read);
    public static readonly StreamPermission Append        = Create<StreamPermission>("streams:append", Operations.Streams.Write);
    public static readonly StreamPermission Delete        = Create<StreamPermission>("streams:delete", Operations.Streams.Delete);
    public static readonly StreamPermission ReadMetadata  = Create<StreamPermission>("streams:metadata:read", Operations.Streams.MetadataRead);
    public static readonly StreamPermission WriteMetadata = Create<StreamPermission>("streams:metadata:write", Operations.Streams.MetadataWrite);

    public static readonly StreamPermission[] All = [Read, Append, Delete, ReadMetadata, WriteMetadata];

    public StreamPermission WithStream(string stream) =>
        Create<StreamPermission>(Value, Operation.WithParameter(StreamId(stream)));
}
