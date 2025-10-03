// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using KurrentDB.Api.Errors;

namespace KurrentDB.Api.Infrastructure.Authorization;

[PublicAPI]
public static class AuthorizationExtensions {
    public static async Task AuthorizeOperation(this IAuthorizationProvider authz, Permission permission, ClaimsPrincipal user, CancellationToken ct) {
        var accessGranted = await authz.CheckAccessAsync(user, permission, ct);
        if (!accessGranted)
            throw ApiErrors.AccessDenied(permission, user.Identity?.Name);
    }

    public static Task AuthorizeOperation(this IAuthorizationProvider authz, Permission permission, ServerCallContext context) =>
        authz.AuthorizeOperation(permission, context.GetHttpContext().User, context.CancellationToken);
}

// public static async Task AuthorizeOperation(this IAuthorizationProvider authz, Permission permission, ClaimsPrincipal user, CancellationToken ct) {
//     var accessGranted = await authz.CheckAccessAsync(user, permission, ct);
//     if (!accessGranted)
//         throw ApiErrors.AccessDenied(permission, user.Identity?.Name);
//
//     ApiErrors.AccessDenied(CallContext.Method, CallContext.GetHttpContext().User.Identity?.Name)
// }
