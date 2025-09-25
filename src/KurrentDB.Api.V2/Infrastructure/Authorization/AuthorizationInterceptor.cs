// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using Grpc.Core.Interceptors;
using KurrentDB.Api.Infrastructure.Grpc.Interceptors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Api.Infrastructure.Authorization;

public class AuthorizationInterceptor(IAuthorizationService authService, ILogger<AuthorizationInterceptor> logger) : OnRequestInterceptor {

    protected override async ValueTask Intercept<TRequest>(TRequest request, ServerCallContext context) {
        var user = context.GetHttpContext().User;
        var temp = user.FindFirstValue(ClaimTypes.Name) ?? "(anonymous)";

        logger.LogInformation("Authorizing {Method} for user {User}", context.Method, user.Identity?.Name ?? "anonymous");

        //System.Security.Claims.ClaimTypes.Anonymous;

        var operationToClaimsMap = new Dictionary<string, string> {
            { "/kurrentdb.api.v2.Streams/AppendSession", "streams:write" }
        };


        await authService.AuthorizeAsync(user, null, new Operation().ToAuthorizationRequirement());

    }

    static string GetIdentity(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name) ?? "(anonymous)";

    // async Task Authorize<TRequest>(ServerCallContext context) where TRequest : class {
    //     var user = context.GetHttpContext().User;
    //     var temp = user.FindFirstValue(ClaimTypes.Name) ?? "(anonymous)";
    //
    //     logger.LogInformation("Authorizing {Method} for user {User}", context.Method, user.Identity?.Name ?? "anonymous");
    //
    //
    //     //System.Security.Claims.ClaimTypes.Anonymous;
    //
    //     var operationToClaimsMap = new Dictionary<string, string> {
    //         { "/kurrentdb.api.v2.Streams/AppendSession", "streams:write" }
    //     };
    //
    //
    //     await authService.AuthorizeAsync(user, null, new Operation().ToAuthorizationRequirement());
    //
    //     return Task.CompletedTask;
    //     // var user = context.GetHttpContext().User;
    //     // if (user?.Identity is not { IsAuthenticated: true })
    //     //     throw new RpcException(new Status(StatusCode.Unauthenticated, "Unauthenticated"));
    //
    //     // I need to somehow match the context.Method to an operation
    //     // and extract parameters from the request
    //     // e.g. for AppendToStream, I need the stream name from the request
    //
    //     // Operation operation = new Operation(); //Operations.Streams
    //
    //     // operation = context.Method switch {
    //     //     ""        => new Operation("stream", "create").WithParameter("stream-name", r.Name),
    //     //     request.Descriptor. r => new Operation("stream", "read").WithParameter("stream-name", r.Name),
    //     //     _         => throw new RpcException(new Status(StatusCode.PermissionDenied, "Unknown operation"))
    //     // };
    //
    //     // var isAuthorized = await auth.CheckAccessAsync(user, operation, context.CancellationToken);
    //     // if (!isAuthorized)
    //     //     throw ApiErrors.AccessDenied("streams:append-session");
    // }

}
