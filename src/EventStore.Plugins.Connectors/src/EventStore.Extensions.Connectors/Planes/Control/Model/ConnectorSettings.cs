namespace EventStore.Connectors.Control;

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