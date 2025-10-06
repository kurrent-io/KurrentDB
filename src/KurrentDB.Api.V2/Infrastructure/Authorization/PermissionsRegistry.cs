// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using KurrentDB.Api.Streams.Authorization;

namespace KurrentDB.Api.Infrastructure.Authorization;

public static class PermissionsRegistry {
    static readonly Dictionary<string, Permission> Permissions = new Dictionary<string, Permission>(StringComparer.OrdinalIgnoreCase) {
        { "/kurrentdb.protocol.v2.streams/StreamsService/AppendSession", StreamPermission.Append }
    };

    public static Permission? GetPermission(string method) =>
        Permissions.TryGetValue(method, out var permission) && permission != Permission.None
            ? permission : null;

    public static Permission? GetPermission(ServerCallContext context) =>
        GetPermission(context.Method);

    public static Permission GetRequiredPermission(string method) =>
        Permissions.TryGetValue(method, out var permission) && permission != Permission.None
            ? permission : throw new InvalidOperationException($"No permission defined for method '{method}'.");

    public static Permission GetRequiredPermission(ServerCallContext context) =>
        GetRequiredPermission(context.Method);
}
