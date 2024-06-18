// using System.Net;
// using DotNext.Collections.Generic;
// using EventStore.Connectors.Control.Activation;
// using EventStore.Connectors.Control.Assignment;
// using EventStore.Core.Cluster;
// using EventStore.Core.Data;
// using EventStore.Core.Messages;
// using Microsoft.Extensions.Logging;
// using static EventStore.Connectors.Control.Assignment.ConnectorAssignmentStrategy;
//
// using ManagementContracts   = EventStore.Connectors.Management.Contracts.Events;
// using ControlPlaneContracts = EventStore.Connectors.Control.Contracts;
//
// namespace EventStore.Connectors.Control.Coordination;
//
// public class ConnectorsCoordinator : ControlPlaneProcessingModule {
//     public ConnectorsCoordinator(GetConnectorAssignor getConnectorAssignor, GetManagedConnectors getManagedConnectors, ConnectorsActivator activator) {
//         Process<GossipUpdatedInMemory>(
// 	        async (evt, ctx) => {
//                 // if the topology is unknown, the node is new, and we have no connectors loaded
//                 if (State.CurrentTopology.IsUnknown) {
//                     var activeConnectors = await getManagedConnectors(ctx.CancellationToken);
//                     State.Connectors = new(activeConnectors.ToDictionary(x => x.ConnectorId));
//
//                     Diagnostics.Publish("ConnectorsLoaded", new {
//                         NodeInstanceId = NodeInstance.InstanceId,
//                         Connectors     = activeConnectors,
//                         Timestamp      = DateTimeOffset.UtcNow
//                     });
//                 }
//
//                 // update the current topology
//                 State.CurrentTopology = evt.ToClusterTopology();
//
//                 Diagnostics.Publish("ClusterTopologyChanged", new {
//                     NodeInstanceId = NodeInstance.InstanceId,
//                     Nodes          = State.CurrentTopology,
//                     Timestamp      = DateTimeOffset.UtcNow
//                 });
//
//                 if (!NodeInstance.IsLeader) {
//                     ctx.Logger.LogTrace(
//                         "{NodeInstanceId} {State} not a leader, ignoring {MessageName}",
//                         NodeInstance.InstanceId, NodeInstance.MemberInfo.State, nameof(GossipUpdatedInMemory)
//                     );
//
//                     return;
//                 }
//
//                 // *******************************
//                 // * trigger rebalance
//                 // *******************************
//                 State.SetNewAssignment(
//                     getConnectorAssignor(StickyWithAffinity).Assign(
//                         State.CurrentTopology,
//                         State.Connectors.Values.Select(x => x.Resource),
//                         State.CurrentAssignment
//                     )
//                 );
//
//                 Diagnostics.Publish("NewConnectorsAssignment", new {
//                     NodeInstanceId   = NodeInstance.InstanceId,
//                     Assignment       = State.CurrentAssignment,
//                     PendingTransfers = State.PendingTransfers,
//                     Timestamp        = DateTimeOffset.UtcNow
//                 });
//
//                 // request activation of all newly assigned connectors
//                 var activationRequests = State.PendingTransfers
//                     .GetNewlyAssignedConnectorsByCluster()
//                     .Select(x => new ControlPlaneContracts.Activation.ActivateConnectors {
//                         ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
//                         NodeId       = x.Key,
//                         Connectors   = { x.Value.Select(cid => State.Connectors[cid].MapToConnector()) }
//                     });
//
//                 activationRequests.ForEach(ctx.Output); // call http endpoint of the cluster node.
//
//
//                 /// just wait a bit to see if the node that died came back or not
//
//
//                 // request deactivation of all revoked connectors, regardless of the fact that they might be reassigned
//                 var deactivationRequests = State.PendingTransfers
//                     .GetRevokedConnectorsByCluster()
//                     .Select(x => new ControlPlaneContracts.Activation.DeactivateConnectors {
//                         ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
//                         NodeId       = x.Key,
//                         Connectors   = { x.Value.Select(cid => State.Connectors[cid].MapToConnector()) }
//                     });
//
//                 deactivationRequests.ForEach(ctx.Output);
//             }
//         );
//
//         Process<ManagementContracts.ConnectorActivating>(
//             async (evt, ctx) => {
//                 ConnectorId connectorId = evt.ConnectorId;
//
//                 if (NodeInstance.IsLeader && State.Rebalancing) {
//                     // TODO SS: How to handle connector activation requests while rebalancing?
//                 }
//
//                 // *******************************
//                 // * update state
//                 // *******************************
//                 State.Connectors.AddOrUpdate(
//                     connectorId,
//                     static (connectorId, state) => new(
//                         connectorId,
//                         state.Revision,
//                         state.Settings,
//                         state.Position,
//                         state.Timestamp
//                     ),
//                     static (_, existing, state) => existing with {
//                         Revision  = state.Revision,
//                         Settings  = state.Settings,
//                         Position  = state.Position,
//                         Timestamp = state.Timestamp
//                     },
//                     (
//                         Revision: evt.Revision,
//                         Settings: new ConnectorSettings(evt.Settings.ToDictionary()),
//                         Position: ctx.Record.LogPosition,
//                         Timestamp: evt.Timestamp.ToDateTimeOffset()
//                     )
//                 );
//
//                 if (NodeInstance.IsNotLeader) {
//                     ctx.Logger.LogTrace(
//                         "{NodeInstanceId} {State} not a leader, ignoring {MessageName}",
//                         NodeInstance.InstanceId, NodeInstance.MemberInfo.State, nameof(ManagementContracts.ConnectorActivating)
//                     );
//                     return;
//                 }
//
//                 Diagnostics.Publish(evt);
//
//                 // *******************************
//                 // * trigger rebalance
//                 // *******************************
//                 State.SetNewAssignment(
//                     getConnectorAssignor(StickyWithAffinity).Assign(
//                         State.CurrentTopology,
//                         State.Connectors.Values.Select(x => x.Resource),
//                         State.CurrentAssignment
//                     )
//                 );
//
//                 Diagnostics.Publish("NewConnectorsAssignment", new {
//                     NodeInstanceId   = NodeInstance.InstanceId,
//                     Assignment       = State.CurrentAssignment,
//                     PendingTransfers = State.PendingTransfers,
//                     Timestamp        = DateTimeOffset.UtcNow
//                 });
//
//                 //TODO SS: Double check if the only possible newly assigned connectors are the single one being activated
//                 // request activation of all newly assigned connectors
//                 var activationRequests = State.PendingTransfers
//                     .GetNewlyAssignedConnectorsByCluster()
//                     .Select(x => new ControlPlaneContracts.Activation.ActivateConnectors {
//                         ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
//                         NodeId       = x.Key,
//                         Connectors   = { x.Value.Select(cid => State.Connectors[cid].MapToConnector()) }
//                     });
//
//                 activationRequests.ForEach(ctx.Output);
//
//                 // request deactivation of all revoked connectors
//                 var deactivationRequests = State.PendingTransfers
//                     .GetRevokedConnectorsByCluster()
//                     .Select(x => new ControlPlaneContracts.Activation.DeactivateConnectors {
//                         ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
//                         NodeId       = x.Key,
//                         Connectors   = { x.Value.Select(cid => State.Connectors[cid].MapToConnector()) }
//                     });
//
//                 deactivationRequests.ForEach(ctx.Output);
//             }
//         );
//
//         Process<ManagementContracts.ConnectorDeactivating>(
//             async (evt, ctx) => {
//                 ConnectorId connectorId = evt.ConnectorId;
//
//                 if (NodeInstance.IsLeader && State.Rebalancing) {
//                     // TODO SS: How to handle connector activation requests while rebalancing?
//                 }
//
//                 // *******************************
//                 // * update state
//                 // *******************************
//                 State.Connectors.Remove(connectorId, out _);
//
//                 if (NodeInstance.IsNotLeader) {
//                     ctx.Logger.LogTrace(
//                         "{NodeInstanceId} {State} not a leader, ignoring {MessageName}",
//                         NodeInstance.InstanceId, NodeInstance.MemberInfo.State, nameof(ManagementContracts.ConnectorDeactivating)
//                     );
//                     return;
//                 }
//
//                 Diagnostics.Publish(evt);
//
//                 // *******************************
//                 // * trigger rebalance
//                 // *******************************
//                 State.SetNewAssignment(
//                     getConnectorAssignor(StickyWithAffinity).Assign(
//                         State.CurrentTopology,
//                         State.Connectors.Values.Select(x => x.Resource),
//                         State.CurrentAssignment
//                     )
//                 );
//
//                 Diagnostics.Publish("NewConnectorsAssignment", new {
//                     NodeInstanceId   = NodeInstance.InstanceId,
//                     Assignment       = State.CurrentAssignment,
//                     PendingTransfers = State.PendingTransfers,
//                     Timestamp        = DateTimeOffset.UtcNow
//                 });
//
//                 // request deactivation of all revoked connectors
//                 var deactivationRequests = State.PendingTransfers
//                     .GetRevokedConnectorsByCluster()
//                     .Select(x => new ControlPlaneContracts.Activation.DeactivateConnectors {
//                         ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
//                         NodeId     = x.Key,
//                         Connectors = { x.Value.Select(cid => State.Connectors[cid].MapToConnector()) }
//                     });
//
//                 deactivationRequests.ForEach(ctx.Output);
//             }
//         );
//
//         ProcessOnLeaderNode<ControlPlaneContracts.Activation.ConnectorsDeactivated>(
//             async (evt, ctx) => {
//                 Diagnostics.Publish(evt);
//
//                 var deactivatedConnectors  = evt.Connectors.ToConnectorIds();
//                 var assignedNodesByCluster = State.PendingTransfers.GetAssignedNodesByCluster(deactivatedConnectors);
//
//                 //  log connectors that are permanently deactivated
//                 if (assignedNodesByCluster.TryGetValue(ClusterNodeId.None, out var deadConnectors)) {
//                     State.PendingTransfers.Complete(deadConnectors.ToArray());
//
//                     ctx.Logger.LogDebug(
//                         "({Count}) connectors permanently deactivated on node {NodeId}: {Connectors}",
//                         deadConnectors.Count, evt.NodeId, deadConnectors
//                     );
//                 }
//
//                 // continue with the rebalance by requesting activation of all connectors that are being reassigned
//                 foreach (var (nodeId, connectors) in assignedNodesByCluster.Where(x => x.Key != ClusterNodeId.None)) {
//                     ctx.Logger.LogDebug(
//                         "({Count}) connectors deactivated on node {NodeId} and awaiting activation: {Connectors}",
//                         connectors.Count, evt.NodeId, connectors.Select(x => $"{x} >> {nodeId} node")
//                     );
//
//                     ctx.Output(new ControlPlaneContracts.Activation.ActivateConnectors {
//                         ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
//                         NodeId     = nodeId,
//                         Connectors = { connectors.Select(cid => State.Connectors[cid].MapToConnector()) }
//                     });
//                 }
//             }
//         );
//
//         ProcessOnLeaderNode<ControlPlaneContracts.Activation.ConnectorsActivated>(
//             async (evt, ctx) => {
//                 Diagnostics.Publish(evt);
//
//                 var connectors = evt.Connectors.ToConnectorIds();
//                 var transfers  = State.PendingTransfers.Complete(connectors);
//
//                 ctx.Logger.LogDebug(
//                     "({Count}) connectors activated on node {NodeId}: {Connectors}",
//                     transfers.Count, evt.NodeId, connectors
//                 );
//             }
//         );
//
//         Process<ControlPlaneContracts.Coordination.RebalanceConnectors>(
//             async (cmd, ctx) => {
//                 if (!NodeInstance.IsLeader) {
//                     ctx.Logger.LogTrace(
//                         "{NodeInstanceId} {State} not a leader, ignoring {MessageName}",
//                         NodeInstance.InstanceId,
//                         NodeInstance.MemberInfo.State,
//                         nameof(GossipUpdatedInMemory)
//                     );
//
//                     return;
//                 }
//
//                 // *******************************
//                 // * trigger rebalance
//                 // *******************************
//                 State.SetNewAssignment(
//                     getConnectorAssignor(StickyWithAffinity).Assign(
//                         State.CurrentTopology,
//                         State.Connectors.Values.Select(x => x.Resource),
//                         State.CurrentAssignment
//                     )
//                 );
//
//                 Diagnostics.Publish(
//                     "NewConnectorsAssignment",
//                     new {
//                         NodeInstanceId   = NodeInstance.InstanceId,
//                         Assignment       = State.CurrentAssignment,
//                         PendingTransfers = State.PendingTransfers,
//                         Timestamp        = DateTimeOffset.UtcNow
//                     }
//                 );
//
//                 // request activation of all newly assigned connectors
//                 var activationRequests = State.PendingTransfers
//                     .GetNewlyAssignedConnectorsByCluster()
//                     .Select(
//                         x => new ControlPlaneContracts.Activation.ActivateConnectors {
//                             ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
//                             NodeId       = x.Key,
//                             Connectors   = { x.Value.Select(cid => State.Connectors[cid].MapToConnector()) }
//                         }
//                     );
//
//                 activationRequests.ForEach(ctx.Output);
//
//                 // request deactivation of all revoked connectors, regardless of the fact that they might be reassigned
//                 var deactivationRequests = State.PendingTransfers
//                     .GetRevokedConnectorsByCluster()
//                     .Select(
//                         x => new ControlPlaneContracts.Activation.DeactivateConnectors {
//                             ActivationId = State.CurrentAssignment.AssignmentId.ToString(),
//                             NodeId       = x.Key,
//                             Connectors   = { x.Value.Select(cid => State.Connectors[cid].MapToConnector()) }
//                         }
//                     );
//
//                 deactivationRequests.ForEach(ctx.Output);
//             }
//         );
//     }
//
//     ConnectorsCoordinatorState State { get; } = new();
// }
//
// [UsedImplicitly]
// record GossipUpdatedInMemory(Guid NodeId, IEnumerable<ClientClusterInfo.ClientMemberInfo> Members) {
// 	public ClusterTopology ToClusterTopology() {
// 		return ClusterTopology.From(MapToClusterNodes(Members));
//
// 		static ClusterNode[] MapToClusterNodes(IEnumerable<ClientClusterInfo.ClientMemberInfo> members) =>
// 			members
// 				.Where(x => x.IsAlive)
// 				.Select(x => new ClusterNode {
//                     NodeId              =  x.InstanceId,
//                     State               = MapToClusterNodeState(x.State),
//                     HttpEndpoint        = new DnsEndPoint(x.HttpEndPointIp, x.HttpEndPointPort),
//                     InternalTcpEndpoint = new DnsEndPoint(x.InternalTcpIp, x.InternalTcpPort)
//                 })
// 				.ToArray();
//
// 		static ClusterNodeState MapToClusterNodeState(VNodeState state) =>
// 			state switch {
// 				VNodeState.Leader          => ClusterNodeState.Leader,
// 				VNodeState.Follower        => ClusterNodeState.Follower,
// 				VNodeState.ReadOnlyReplica => ClusterNodeState.ReadOnlyReplica,
// 				_                          => ClusterNodeState.Unmapped
// 			};
// 	}
// }