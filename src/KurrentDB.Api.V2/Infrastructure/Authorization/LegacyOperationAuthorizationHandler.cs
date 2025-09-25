// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Microsoft.AspNetCore.Authorization;
using ILegacyAuthorizationProvider = EventStore.Plugins.Authorization.IAuthorizationProvider;

namespace KurrentDB.Api.Infrastructure.Authorization;

[PublicAPI]
public record LegacyOperationRequirement(EventStore.Plugins.Authorization.Operation Operation) : IAuthorizationRequirement {
    public static implicit operator EventStore.Plugins.Authorization.Operation(LegacyOperationRequirement requirement) => requirement.Operation;

    public override string ToString() => $"{Operation.Resource}:{Operation.Action}";
}

[PublicAPI]
public class LegacyOperationAuthorizationHandler(ILegacyAuthorizationProvider legacyProvider) : AuthorizationHandler<LegacyOperationRequirement> {
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, LegacyOperationRequirement requirement) {
        var authorized = await legacyProvider.CheckAccessAsync(context.User, requirement.Operation, CancellationToken.None);

        if (authorized)
            context.Succeed(requirement);
        else
            context.Fail();
    }
}

[PublicAPI]
public class StreamsOperationAuthorizationHandler(ILegacyAuthorizationProvider legacyProvider) : AuthorizationHandler<LegacyOperationRequirement, IMessage> {
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, LegacyOperationRequirement requirement, IMessage resource) {

        var authorized = await legacyProvider.CheckAccessAsync(context.User, requirement.Operation, CancellationToken.None);

        if (authorized)
            context.Succeed(requirement);
        else
            context.Fail();
    }
}

public static class LegagyOperationExtensions {
    public static LegacyOperationRequirement ToAuthorizationRequirement(this EventStore.Plugins.Authorization.Operation operation) => new(operation);
}
