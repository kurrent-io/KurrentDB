// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using KurrentDB.Api.Errors;

namespace KurrentDB.Api.Infrastructure;

static class NodeSystemInfoProviderExtensions {
    public static async ValueTask EnsureNodeIsLeader(this NodeSystemInfoProvider provider, CancellationToken cancellationToken) {
        var info = await provider.CheckLeadership(cancellationToken);
        if (info.IsNotLeader)
            throw ApiErrors.NotLeaderNode(info.InstanceId, info.Endpoint);
    }
}
