using System.Diagnostics.Metrics;
using DotNext.Collections.Generic;
using EventStore.Connectors.ControlPlane.Assignment;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using Microsoft.Extensions.Logging;

using static EventStore.Connectors.ControlPlane.Assignment.ConnectorAssignmentStrategy;

using ManagementContracts   = EventStore.Connectors.Management.Contracts.Events;
using ControlPlaneContracts = EventStore.Connectors.ControlPlane.Contracts;

namespace EventStore.Connectors.ControlPlane.Coordination;

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
	
	internal Counter<long> TopologyChanges { get; }
	internal Counter<long> Assigments { get; }
	internal Counter<long> ActivationRequests { get; }
	internal Counter<long> DeactivationRequests { get; }
	internal Counter<long> Activations { get; }
	internal Counter<long> Deactivations { get; }
	internal Histogram<double> ActivationDuration { get; }
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
		catch (Exception) {
			// ignored
		}
	}
	
	// public void TrackActiveConnectors(Guid nodeInstanceId) => ActiveConnectors.Add(1, new KeyValuePair<string, object?>("node_instance_id", nodeInstanceId));
}

public class ConnectorsCoordinator : ControlPlaneProcessingModule {
    public ConnectorsCoordinator(GetConnectorAssignor getConnectorAssignor, ListActiveConnectors listActiveConnectors) {
        Process<GossipUpdatedInMemory>(
	        async (evt, ctx) => {
                // if the topology is unknown, the node is new, and we have no connectors loaded
                if (State.CurrentTopology.IsUnknown) {
                    var activeConnectors = await listActiveConnectors(ctx.CancellationToken);
                    State.Connectors = new(activeConnectors.ToDictionary(x => x.ConnectorId));
                    
                    Diagnostics.Dispatch("ConnectorsLoaded", new {
                        NodeInstanceId = NodeInstance.InstanceId,
                        Connectors     = activeConnectors,
                        Timestamp      = DateTimeOffset.UtcNow
                    });
                }
                
                // update the current topology
                State.CurrentTopology = evt.ToClusterTopology();
                
                Diagnostics.Dispatch("ClusterTopologyChanged", new {
                    NodeInstanceId = NodeInstance.InstanceId,
                    Nodes          = State.CurrentTopology,
                    Timestamp      = DateTimeOffset.UtcNow
                });
                
                if (!NodeInstance.IsLeader) {
                    ctx.Logger.LogTrace(
                        "{NodeInstanceId} {State} not a leader, ignoring {MessageName}", 
                        NodeInstance.InstanceId, NodeInstance.MemberInfo.State, nameof(GossipUpdatedInMemory)
                    );
                    
                    return;
                }

                // *******************************
                // * trigger rebalance
                // *******************************
                State.SetNewAssignment(
                    getConnectorAssignor(StickyWithAffinity).Assign(
                        State.CurrentTopology,
                        State.Connectors.Values.Select(x => x.Resource),
                        State.CurrentAssignment
                    )
                );
                
                Diagnostics.Dispatch("NewConnectorsAssignment", new {
                    NodeInstanceId   = NodeInstance.InstanceId,
                    Assignment       = State.CurrentAssignment,
                    PendingTransfers = State.PendingTransfers,
                    Timestamp        = DateTimeOffset.UtcNow
                });
                    
                // request activation of all newly assigned connectors
                var activationRequests = State.PendingTransfers
                    .GetNewlyAssignedConnectorsByCluster()
                    .Select(x => new ControlPlaneContracts.Activation.ActivateConnectors {
                        ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
                        NodeId       = x.Key,
                        Connectors   = { x.Value.Select(cid => State.Connectors[cid].MapToConnector()) }
                    });
                
                activationRequests.ForEach(ctx.Output);
                
                // request deactivation of all revoked connectors, regardless of the fact that they might be reassigned
                var deactivationRequests = State.PendingTransfers
                    .GetRevokedConnectorsByCluster()
                    .Select(x => new ControlPlaneContracts.Activation.DeactivateConnectors {
                        ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
                        NodeId       = x.Key,
                        Connectors   = { x.Value.Select(cid => State.Connectors[cid].MapToConnector()) }
                    });
                
                deactivationRequests.ForEach(ctx.Output);
            }
        );

        Process<ManagementContracts.ConnectorActivating>(
            async (evt, ctx) => {
                ConnectorId connectorId = evt.ConnectorId;
                
                if (NodeInstance.IsLeader && State.Rebalancing) {
                    // TODO SS: How to handle connector activation requests while rebalancing?
                }
                
                // *******************************
                // * update state
                // *******************************
                State.Connectors.AddOrUpdate(
                    connectorId,
                    static (connectorId, state) => new(
                        connectorId, 
                        state.Revision, 
                        state.Settings, 
                        state.Position, 
                        state.Timestamp
                    ),
                    static (_, existing, state) => existing with {
                        Revision  = state.Revision,
                        Settings  = state.Settings,
                        Position  = state.Position,
                        Timestamp = state.Timestamp
                    }, 
                    (
                        Revision: evt.Revision, 
                        Settings: new ConnectorSettings(evt.Settings.ToDictionary()),
                        Position: ctx.Record.LogPosition,
                        Timestamp: evt.Timestamp.ToDateTimeOffset()
                    )
                );

                if (NodeInstance.IsNotLeader) {
                    ctx.Logger.LogTrace(
                        "{NodeInstanceId} {State} not a leader, ignoring {MessageName}", 
                        NodeInstance.InstanceId, NodeInstance.MemberInfo.State, nameof(ManagementContracts.ConnectorActivating)
                    );
                    return;
                }
                
                Diagnostics.Dispatch(evt);
                
                // *******************************
                // * trigger rebalance
                // *******************************
                State.SetNewAssignment(
                    getConnectorAssignor(StickyWithAffinity).Assign(
                        State.CurrentTopology,
                        State.Connectors.Values.Select(x => x.Resource),
                        State.CurrentAssignment
                    )
                );
                
                Diagnostics.Dispatch("NewConnectorsAssignment", new {
                    NodeInstanceId   = NodeInstance.InstanceId,
                    Assignment       = State.CurrentAssignment,
                    PendingTransfers = State.PendingTransfers,
                    Timestamp        = DateTimeOffset.UtcNow
                });
                
                //TODO SS: Double check if the only possible newly assigned connectors are the single one being activated
                // request activation of all newly assigned connectors
                var activationRequests = State.PendingTransfers
                    .GetNewlyAssignedConnectorsByCluster()
                    .Select(x => new ControlPlaneContracts.Activation.ActivateConnectors {
                        ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
                        NodeId       = x.Key,
                        Connectors   = { x.Value.Select(cid => State.Connectors[cid].MapToConnector()) }
                    });
                
                activationRequests.ForEach(ctx.Output);
                
                // request deactivation of all revoked connectors
                var deactivationRequests = State.PendingTransfers
                    .GetRevokedConnectorsByCluster()
                    .Select(x => new ControlPlaneContracts.Activation.DeactivateConnectors {
                        ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
                        NodeId       = x.Key,
                        Connectors   = { x.Value.Select(cid => State.Connectors[cid].MapToConnector()) }
                    });
                
                deactivationRequests.ForEach(ctx.Output);
            }
        );
        
        Process<ManagementContracts.ConnectorDeactivating>(
            async (evt, ctx) => {
                ConnectorId connectorId = evt.ConnectorId;
                
                if (NodeInstance.IsLeader && State.Rebalancing) {
                    // TODO SS: How to handle connector activation requests while rebalancing?
                }

                // *******************************
                // * update state
                // *******************************
                State.Connectors.Remove(connectorId, out _);
                
                if (NodeInstance.IsNotLeader) {
                    ctx.Logger.LogTrace(
                        "{NodeInstanceId} {State} not a leader, ignoring {MessageName}", 
                        NodeInstance.InstanceId, NodeInstance.MemberInfo.State, nameof(ManagementContracts.ConnectorDeactivating)
                    );
                    return;
                }
                
                Diagnostics.Dispatch(evt);

                // *******************************
                // * trigger rebalance
                // *******************************
                State.SetNewAssignment(
                    getConnectorAssignor(StickyWithAffinity).Assign(
                        State.CurrentTopology,
                        State.Connectors.Values.Select(x => x.Resource),
                        State.CurrentAssignment
                    )
                );
                
                Diagnostics.Dispatch("NewConnectorsAssignment", new {
                    NodeInstanceId   = NodeInstance.InstanceId,
                    Assignment       = State.CurrentAssignment,
                    PendingTransfers = State.PendingTransfers,
                    Timestamp        = DateTimeOffset.UtcNow
                });
                
                // request deactivation of all revoked connectors
                var deactivationRequests = State.PendingTransfers
                    .GetRevokedConnectorsByCluster()
                    .Select(x => new ControlPlaneContracts.Activation.DeactivateConnectors {
                        ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
                        NodeId     = x.Key,
                        Connectors = { x.Value.Select(cid => State.Connectors[cid].MapToConnector()) }
                    });
                
                deactivationRequests.ForEach(ctx.Output);
            }
        );
    
        ProcessOnLeaderNode<ControlPlaneContracts.Activation.ConnectorsDeactivated>(
            async (evt, ctx) => {
                Diagnostics.Dispatch(evt);
                
                var deactivatedConnectors  = evt.Connectors.ToConnectorIds();
                var assignedNodesByCluster = State.PendingTransfers.GetAssignedNodesByCluster(deactivatedConnectors);

                //  log connectors that are permanently deactivated
                if (assignedNodesByCluster.TryGetValue(ClusterNodeId.None, out var deadConnectors)) {
                    State.PendingTransfers.Complete(deadConnectors.ToArray());
                        
                    ctx.Logger.LogDebug(
                        "({Count}) connectors permanently deactivated on node {NodeId}: {Connectors}",
                        deadConnectors.Count, evt.NodeId, deadConnectors
                    );
                }
                
                // continue with the rebalance by requesting activation of all connectors that are being reassigned
                foreach (var (nodeId, connectors) in assignedNodesByCluster.Where(x => x.Key != ClusterNodeId.None)) {
                    ctx.Logger.LogDebug(
                        "({Count}) connectors deactivated on node {NodeId} and awaiting activation: {Connectors}",
                        connectors.Count, evt.NodeId, connectors.Select(x => $"{x} >> {nodeId} node")
                    );
                    
                    ctx.Output(new ControlPlaneContracts.Activation.ActivateConnectors {
                        ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
                        NodeId     = nodeId,
                        Connectors = { connectors.Select(cid => State.Connectors[cid].MapToConnector()) } 
                    });
                }
            }
        );
        
        ProcessOnLeaderNode<ControlPlaneContracts.Activation.ConnectorsActivated>(
            async (evt, ctx) => {
                Diagnostics.Dispatch(evt);
                
                var connectors = evt.Connectors.ToConnectorIds();
                var transfers  = State.PendingTransfers.Complete(connectors);
                
                ctx.Logger.LogDebug(
                    "({Count}) connectors activated on node {NodeId}: {Connectors}",
                    transfers.Count, evt.NodeId, connectors
                );
            }
        );
    }

    ConnectorsCoordinatorState State { get; } = new();
}

    
[UsedImplicitly]
record GossipUpdatedInMemory(Guid NodeId, IEnumerable<ClientClusterInfo.ClientMemberInfo> Members) {
	public ClusterTopology ToClusterTopology() {
		return ClusterTopology.From(MapToClusterNodes(Members));
            
		static ClusterNode[] MapToClusterNodes(IEnumerable<ClientClusterInfo.ClientMemberInfo> members) =>
			members
				.Where(x => x.IsAlive)
				.Select(x => new ClusterNode(x.InstanceId, MapToClusterNodeState(x.State)))
				.ToArray();

		static ClusterNodeState MapToClusterNodeState(VNodeState state) =>
			state switch {
				VNodeState.Leader          => ClusterNodeState.Leader,
				VNodeState.Follower        => ClusterNodeState.Follower,
				VNodeState.ReadOnlyReplica => ClusterNodeState.ReadOnlyReplica,
				_                          => ClusterNodeState.Unmapped
			};
	}
}