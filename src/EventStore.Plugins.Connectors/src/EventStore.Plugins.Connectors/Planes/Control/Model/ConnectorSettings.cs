namespace EventStore.Connectors.Control;

public class ConnectorSettings(IDictionary<string, string?> settings) : Dictionary<string, string?>(settings) {
    public ClusterNodeState NodeAffinity {
        get => TryGetValue("Subscription:NodeAffinity", out var value)
            ? value switch {
                "Leader"          => ClusterNodeState.Leader,
                "Follower"        => ClusterNodeState.Follower,
                "ReadOnlyReplica" => ClusterNodeState.ReadOnlyReplica,
                _                 => ClusterNodeState.Unmapped
            }
            : ClusterNodeState.Unmapped;
    }
}