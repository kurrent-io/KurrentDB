// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using Humanizer;
using KurrentDB.Api.Errors;

namespace KurrentDB.Api.Infrastructure.Authorization;

[PublicAPI]
public static class AuthorizationExtensions {
    public static async Task AuthorizeOperation(this IAuthorizationProvider authz, Operation operation, ClaimsPrincipal user, CancellationToken ct) {
        var accessGranted = await authz.CheckAccessAsync(user, operation, ct);
        if (!accessGranted)
            throw ApiErrors.AccessDenied(operation.GetClaimName(), user.Identity?.Name);
    }

    public static Task AuthorizeOperation(this IAuthorizationProvider authz, Operation operation, ServerCallContext context) =>
        authz.AuthorizeOperation(operation, context.GetHttpContext().User, context.CancellationToken);

    public static Task AuthorizeOperation(this IAuthorizationProvider authz, OperationDefinition operation, Parameter resourceId, ClaimsPrincipal user, CancellationToken ct) =>
         authz.AuthorizeOperation(new Operation(operation, resourceId), user, ct);

    public static Task AuthorizeOperation(this IAuthorizationProvider authz, OperationDefinition operation, Parameter resourceId, ServerCallContext context) =>
        authz.AuthorizeOperation(new Operation(operation, resourceId), context);
}

public static class OperationsExtensions {
    public static string GetClaimName(this Operation operation) =>
        ClaimName.Create(operation.Resource, operation.Action);
}

public static class ClaimName {
    /// <summary>
    /// Creates a claim name based on the given resource and action.
    /// The resource can be a scope or a chain (e.g., "streams", "node/scavenge"), and the action will be converted to snake_case.
    /// The resulting format will be "resource:action" (e.g., "streams:append").
    /// </summary>
    public static string Create(string resource, string action) {
        // Replace '/' with ':' in resource and convert action to snake_case using Humanizer
        var processedResource = resource.Replace('/', ':');
        var snakeCaseAction   = action.Underscore();
        return $"{processedResource}:{snakeCaseAction}";
    }
}
