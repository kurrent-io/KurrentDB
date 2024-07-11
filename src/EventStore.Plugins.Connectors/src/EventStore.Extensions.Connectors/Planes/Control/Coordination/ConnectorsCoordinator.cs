using System.Net;
using EventStore.Core.Cluster;
using EventStore.Core.Data;

namespace EventStore.Connectors.Control.Coordination;

public class ConnectorsCoordinator : ControlPlaneProcessingModule {
    // public ConnectorsCoordinator(GetActiveConnectors getActiveConnectors, ConnectorsActivator activator) {
    //     Process<SystemMessage.BecomeLeader>(
    //         async (_, ctx) => {
    //             // // if the topology is unknown, the node is new, and we have no connectors loaded
    //             // if (State.CurrentTopology.IsUnknown) {
    //             //     var activeConnectors = await getManagedConnectors(ctx.CancellationToken);
    //             //     State.Connectors = new(activeConnectors.ToDictionary(x => x.ConnectorId));
    //             //
    //             //     Diagnostics.Publish(
    //             //         "ConnectorsLoaded",
    //             //         new {
    //             //             NodeInstanceId = NodeInstance.InstanceId,
    //             //             Connectors     = activeConnectors,
    //             //             Timestamp      = DateTimeOffset.UtcNow
    //             //         }
    //             //     );
    //             // }
    //
    //             var activeConnectors = await getActiveConnectors(ctx.CancellationToken);
    //             State.Connectors = new(activeConnectors.ToDictionary(x => x.ConnectorId));
    //
    //             Diagnostics.Publish("ConnectorsLoaded", new {
    //                 NodeInstanceId = NodeInstance.InstanceId,
    //                 Connectors     = activeConnectors,
    //                 Timestamp      = DateTimeOffset.UtcNow
    //             });
    //
    //             var activationRequest = new ControlPlaneContracts.Activation.ActivateConnectors {
    //                 ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
    //                 NodeId       = NodeInstance.InstanceId.ToString(),
    //                 Connectors   = { State.Connectors.Select(x => x.Value.MapToConnector()) }
    //             };
    //
    //             await activator.Activate(activationRequest, ctx.CancellationToken);
    //         });
    //
    //     Process<SystemMessage.BecomeResigningLeader>(
    //         async (evt, ctx) => {
    //             //await activator.DeactivateAll();
    //         });
    //
    //     Process<GossipUpdatedInMemory>(
    //         async (evt, ctx) => {
    //             // if the topology is unknown, the node is new, and we have no connectors loaded
    //             if (State.CurrentTopology.IsUnknown) {
    //                 var activeConnectors = await getActiveConnectors(ctx.CancellationToken);
    //                 State.Connectors = new(activeConnectors.ToDictionary(x => x.ConnectorId));
    //
    //                 Diagnostics.Publish(
    //                     "ConnectorsLoaded",
    //                     new {
    //                         NodeInstanceId = NodeInstance.InstanceId,
    //                         Connectors     = activeConnectors,
    //                         Timestamp      = DateTimeOffset.UtcNow
    //                     }
    //                 );
    //             }
    //
    //             // update the current topology
    //             State.CurrentTopology = evt.ToClusterTopology();
    //
    //             Diagnostics.Publish(
    //                 "ClusterTopologyChanged",
    //                 new {
    //                     NodeInstanceId = NodeInstance.InstanceId,
    //                     Nodes          = State.CurrentTopology,
    //                     Timestamp      = DateTimeOffset.UtcNow
    //                 }
    //             );
    //         }
    //     );
    //
    //     ProcessOnLeaderNode<ManagementContracts.ConnectorActivating>(
    //         async (evt, ctx) => {
    //             ConnectorId connectorId = evt.ConnectorId;
    //
    //             // *******************************
    //             // * update state
    //             // *******************************
    //             State.Connectors.AddOrUpdate(
    //                 connectorId,
    //                 static (connectorId, state) => new(
    //                     connectorId,
    //                     state.Revision,
    //                     state.Settings
    //                 ),
    //                 static (_, existing, state) => existing with {
    //                     Revision  = state.Revision,
    //                     Settings  = state.Settings,
    //                 },
    //                 (
    //                     Revision: evt.Revision,
    //                     Settings: new ConnectorSettings(evt.Settings.ToDictionary()),
    //                     Position: ctx.Record.LogPosition,
    //                     Timestamp: evt.Timestamp.ToDateTimeOffset()
    //                 )
    //             );
    //
    //             await activator.Activate(
    //                 new ControlPlaneContracts.Activation.ActivateConnectors {
    //                     ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
    //                     NodeId       = NodeInstance.InstanceId.ToString(),
    //                     Connectors   = { State.Connectors[connectorId].MapToConnector() }
    //                 }
    //             );
    //         }
    //     );
    //
    //     ProcessOnLeaderNode<ManagementContracts.ConnectorDeactivating>(
    //         async (evt, ctx) => {
    //             ConnectorId connectorId = evt.ConnectorId;
    //
    //             // *******************************
    //             // * update state
    //             // *******************************
    //             State.Connectors.Remove(connectorId, out _);
    //
    //             await activator.Deactivate(new ControlPlaneContracts.Activation.DeactivateConnectors {
    //                 ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
    //                 NodeId       = NodeInstance.InstanceId.ToString(),
    //                 Connectors   = { State.Connectors[connectorId].MapToConnector() }
    //             });
    //         }
    //     );
    // }

    ConnectorsCoordinatorState State { get; } = new();
}

[UsedImplicitly]
record GossipUpdatedInMemory(Guid NodeId, IEnumerable<ClientClusterInfo.ClientMemberInfo> Members) {
	public ClusterTopology ToClusterTopology() {
		return ClusterTopology.From(MapToClusterNodes(Members));

		static ClusterNode[] MapToClusterNodes(IEnumerable<ClientClusterInfo.ClientMemberInfo> members) =>
			members
				.Where(x => x.IsAlive)
				.Select(x => new ClusterNode {
                    NodeId              =  x.InstanceId,
                    State               = MapToClusterNodeState(x.State),
                    HttpEndpoint        = new DnsEndPoint(x.HttpEndPointIp, x.HttpEndPointPort),
                    InternalTcpEndpoint = new DnsEndPoint(x.InternalTcpIp, x.InternalTcpPort)
                })
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