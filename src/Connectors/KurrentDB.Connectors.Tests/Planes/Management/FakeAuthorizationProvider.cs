// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using EventStore.Plugins.Authorization;

namespace KurrentDB.Connectors.Tests.Planes.Management;

public class FakeAuthorizationProvider : AuthorizationProviderBase {
    public bool ShouldGrantAccess { get; set; } = true;

    public override ValueTask<bool> CheckAccessAsync(
        ClaimsPrincipal principal,
        Operation operation,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult(ShouldGrantAccess);
}