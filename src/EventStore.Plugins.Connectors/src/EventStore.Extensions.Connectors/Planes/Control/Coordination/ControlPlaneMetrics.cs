using System.Diagnostics.Metrics;

namespace EventStore.Connectors.Control.Coordination;

public class ControlPlaneMetrics {
    public ControlPlaneMetrics(IMeterFactory meterFactory) {
        //should I have a meter per component? or can I make these generic?

        Meter = meterFactory.Create("EventStore.Connectors.ControlPlane");

        const string prefix = "eventstore.connectors.control_plane.";

        TopologyChanges      = Meter.CreateCounter<long>($"{prefix}topology_changes", description: "Number of topology changes detected by the coordinator (leader)");
        Assigments           = Meter.CreateCounter<long>($"{prefix}assignments", description: "Number of connector assignments made by the coordinator (leader)");
        ActivationRequests   = Meter.CreateCounter<long>($"{prefix}activation_requests", description: "Number of connector activation requests received by the coordinator (leader)");
        DeactivationRequests = Meter.CreateCounter<long>($"{prefix}deactivation_requests", description: "Number of connector deactivation requests received by the coordinator (leader)");
        Activations          = Meter.CreateCounter<long>($"{prefix}activations", description: "Number of connector activations completed by the activator");
        Deactivations        = Meter.CreateCounter<long>($"{prefix}deactivations", description: "Number of connector deactivations completed by the activator");

        ActivationDuration   = Meter.CreateHistogram<double>($"{prefix}connector_activation_duration", description: "Duration of connector activation", unit: "ms");

        // ActiveConnectors = Meter.CreateObservableGauge(
        // 	$"{prefix}active_connectors", description: "Number of active connectors",
        // 	observeValue: () => ActiveConnectorsCount
        // );
    }

    TimeProvider Time { get; } = TimeProvider.System;
    // ConcurrentDictionary<string, long> ConnectorActivationStartTimes { get; } = new();
    // long ActiveConnectorsCount { get; set; }

    internal Meter Meter { get; }

    internal Counter<long>     TopologyChanges      { get; }
    internal Counter<long>     Assigments           { get; }
    internal Counter<long>     ActivationRequests   { get; }
    internal Counter<long>     DeactivationRequests { get; }
    internal Counter<long>     Activations          { get; }
    internal Counter<long>     Deactivations        { get; }
    internal Histogram<double> ActivationDuration   { get; }
    // internal ObservableGauge<long> ActiveConnectors { get; }

    public void TrackTopologyChanges(Guid nodeInstanceId) =>
        TopologyChanges.Add(1, new("node.id", nodeInstanceId), new("node.state", ClusterNodeState.Leader));

    public void TrackAssigments(Guid nodeInstanceId) =>
        Assigments.Add(1, new("node.id", nodeInstanceId), new("node.state", ClusterNodeState.Leader));

    public void TrackActivationRequests(Guid nodeInstanceId) =>
        ActivationRequests.Add(1, new("node.id", nodeInstanceId), new("node.state", ClusterNodeState.Leader));

    public void TrackDeactivationRequests(Guid nodeInstanceId) =>
        DeactivationRequests.Add(1, new("node.id", nodeInstanceId), new("node.state", ClusterNodeState.Leader));

    public void TrackActivations(Guid nodeInstanceId, ClusterNodeState nodeState) =>
        Activations.Add(1, new("node.id", nodeInstanceId), new("node.state", nodeState));

    public void TrackDeactivations(Guid nodeInstanceId, ClusterNodeState nodeState) =>
        Deactivations.Add(1, new("node.id", nodeInstanceId), new("node.state", nodeState));

    // public void StartRecordingConnectorActivationDuration(string connectorId) =>
    // 	ConnectorActivationStartTimes.TryAdd(connectorId, Time.GetTimestamp());
    //
    // public void StopRecordingConnectorActivationDuration(Guid nodeInstanceId, ClusterNodeState nodeState, string connectorId, string connectorType) {
    // 	if (!ConnectorActivationStartTimes.TryRemove(connectorId, out var startTime)) return;
    // 	ActivationDuration.Record(
    // 		Time.GetElapsedTime(startTime).TotalMilliseconds,
    // 		new("node.id", nodeInstanceId),
    // 		new("node.state", nodeState),
    // 		new("connector.id", connectorId),
    // 		new("connector.type_name", connectorType)
    // 	);
    // }

    public async Task RecordConnectorActivationDuration(Func<Task> action, Guid nodeInstanceId, ClusterNodeState nodeState, string connectorId, string connectorType) {
        try {
            var startTime = Time.GetTimestamp();

            await action();

            ActivationDuration.Record(
                Time.GetElapsedTime(startTime).TotalMilliseconds,
                new("node.id", nodeInstanceId),
                new("node.state", nodeState),
                new("connector.id", connectorId),
                new("connector.type_name", connectorType)
            );
        }
        catch {
            // ignored
        }
    }

    // public void TrackActiveConnectors(Guid nodeInstanceId) => ActiveConnectors.Add(1, new KeyValuePair<string, object?>("node_instance_id", nodeInstanceId));
}
