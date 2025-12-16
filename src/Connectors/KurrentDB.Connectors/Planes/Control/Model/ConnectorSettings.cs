// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Connectors.Planes.Control.Model;

public static class ConnectorSettingsExtensions {
    public static ClusterNodeState NodeAffinity(this IDictionary<string, string?> settings) =>
        settings.TryGetValue("Subscription:NodeAffinity", out var value)
            ? value switch {
                "Leader"          => ClusterNodeState.Leader,
                "Follower"        => ClusterNodeState.Follower,
                "ReadOnlyReplica" => ClusterNodeState.ReadOnlyReplica,
                _                 => ClusterNodeState.Unmapped
            }
            : ClusterNodeState.Unmapped;
}
