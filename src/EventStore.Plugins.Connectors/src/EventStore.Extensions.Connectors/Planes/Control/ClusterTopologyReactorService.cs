// // Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// // Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).
//
// using EventStore.Connectors.Control;
// using EventStore.Core.Bus;
// using EventStore.Core.Cluster;
// using EventStore.Core.Data;
// using EventStore.Core.Messages;
// using Microsoft.Extensions.Logging;
//
// namespace EventStore.Connectors.System;
//
// public class ClusterTopologyReactorService(ISubscriber subscriber, IPublisher publisher, SystemReadinessProbe probe, ILogger<ClusterTopologyReactorService> logger) : SystemBackgroundService(probe) {
//     ISubscriber Subscriber { get; } = subscriber;
//     IPublisher  Publisher  { get; } = publisher;
//     ILogger     Logger     { get; } = logger;
//
//     protected override async Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
//         var actualTopology = ClusterTopology.Unknown;
//
//         Subscriber.On<GossipMessage.GossipUpdated>((evt, _) => {
//             var updatedTopology = ClusterTopology.From(MapToClusterNodes(evt.ClusterInfo.Members));
//
//             // sanity check
//             if (updatedTopology != actualTopology) {
//                 Publisher.Publish(new ConnectorsSystemMessages.ClusterTopologyChanged(actualTopology, updatedTopology, DateTimeOffset.UtcNow));
//
//                 actualTopology = updatedTopology;
//
//                 Logger.LogDebug(
//                     "[{NodeId}|{NodeState}] Cluster Topology Change Detected: {GossipMembersInfo}",
//                     nodeInfo.InstanceId, nodeInfo.MemberInfo.State, evt.ClusterInfo.Members.Select(x => (x.State, x.InstanceId))
//                 );
//
//                 return;
//             }
//
//             Logger.LogWarning(
//                 "[{NodeId}|{NodeState}] Gossip updated but no topology changes were detected: {GossipMembersInfo}",
//                 nodeInfo.InstanceId, nodeInfo.MemberInfo.State, evt.ClusterInfo.Members.Select(x => (x.State, x.InstanceId))
//             );
//         });
//     }
//
//     static ClusterNode[] MapToClusterNodes(MemberInfo[] members) =>
//         members
//             .Where(x => x.IsAlive)
//             .Select(x => new ClusterNode {
//                 NodeId = x.InstanceId,
//                 State  = MapToClusterNodeState(x.State)
//             })
//             .ToArray();
//
//     static ClusterNodeState MapToClusterNodeState(VNodeState state) =>
//         state switch {
//             VNodeState.Leader          => ClusterNodeState.Leader,
//             VNodeState.Follower        => ClusterNodeState.Follower,
//             VNodeState.ReadOnlyReplica => ClusterNodeState.ReadOnlyReplica,
//             _                          => ClusterNodeState.Unmapped
//         };
// }