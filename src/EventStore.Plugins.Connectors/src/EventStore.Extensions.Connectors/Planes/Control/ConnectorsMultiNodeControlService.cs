// // Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// // Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).
//
// using EventStore.Connect.Connectors;
// using EventStore.Connect.Consumers.Configuration;
// using EventStore.Connectors.Control;
// using EventStore.Connectors.Control.Assignment;
// using EventStore.Connectors.Management.Contracts;
// using EventStore.Core.Bus;
// using EventStore.Core.Messaging;
// using EventStore.Streaming;
// using Microsoft.Extensions.Logging;
// using static EventStore.Connectors.System.ConnectorsSystemMessages;
//
// namespace EventStore.Connectors.System;
//
// public static class ConnectorsSystemMessages {
//     public class ClusterTopologyChanged(ClusterTopology previousTopology, ClusterTopology actualTopology, DateTimeOffset timestamp) : Message {
//         public ClusterTopology PreviousTopology { get; init; } = previousTopology;
//         public ClusterTopology ActualTopology   { get; init; } = actualTopology;
//         public DateTimeOffset  Timestamp        { get; init; } = timestamp;
//     }
//
//     public class ConnectorsAssignment : Message {
//         /// <summary>
//         /// Connectors already assigned to the node.
//         /// </summary>
//         public RegisteredConnector[] Unchanged { get; set; }
//
//         /// <summary>
//         /// Connectors newly assigned to the node.
//         /// </summary>
//         public RegisteredConnector[] Assigned { get; set; }
//
//         /// <summary>
//         /// Connectors revoked from the node.
//         /// </summary>
//         public RegisteredConnector[] Revoked { get; set; }
//
//         public DateTimeOffset Timestamp { get; set; }
//     }
//
//     public class ConnectorsAssigmentApplied : Message {
//         public RegisteredConnector[] Assigned  { get; set; }
//         public DateTimeOffset        Timestamp { get; set; }
//     }
// }
//
// public class ConnectorsMultiNodeControlService : SystemBackgroundService {
//     public ConnectorsMultiNodeControlService(
//         ISubscriber subscriber,
//         GetActiveConnectors getActiveConnectors,
//         IConnectorAssignor assignor,
//         ConnectorsActivator activator,
//         Func<SystemConsumerBuilder> getConsumerBuilder,
//         SystemReadinessProbe probe,
//         ILogger<ConnectorsMultiNodeControlService> logger
//     ) : base(probe) {
//         Subscriber          = subscriber;
//         GetActiveConnectors = getActiveConnectors;
//         Assignor            = assignor;
//         Activator           = activator;
//         Logger              = logger;
//
//         ConsumerBuilder = getConsumerBuilder()
//             .ConsumerId("conn-ctrl-coordinator-csx")
//             .Filter(ConnectorsFeatureConventions.Filters.ManagementFilter)
//             .InitialPosition(SubscriptionInitialPosition.Latest)
//             .DisableAutoCommit();
//     }
//
//     ISubscriber           Subscriber          { get; }
//     IPublisher            Publisher           { get; }
//     ConnectorsActivator   Activator           { get; }
//     GetActiveConnectors   GetActiveConnectors { get; }
//     IConnectorAssignor    Assignor            { get; }
//     SystemConsumerBuilder ConsumerBuilder     { get; }
//     ILogger               Logger              { get; }
//
//     protected override async Task Execute(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
//         Logger.LogControlServiceRunning(nodeInfo.InstanceId);
//
//         var connectors       = await GetActiveConnectors(stoppingToken);
//         var actualAssignment = new ClusterConnectorsAssignment();
//         var clusterTopology  = ClusterTopology.Unknown;
//
//         Subscriber.On<ClusterTopologyChanged>((topologyChanged, _) => {
//             var newAssignment  = Assignor.Assign(topologyChanged.ActualTopology, connectors.Select(x => x.Resource), actualAssignment);
//             var nodeAssignment = newAssignment[nodeInfo.InstanceId];
//
//             clusterTopology = topologyChanged.ActualTopology;
//
//             Publisher.Publish(new ConnectorsAssignment {
//                 Unchanged = connectors.Where(x => nodeAssignment.Unchanged.Contains(x.ConnectorId)).ToArray(),
//                 Assigned  = connectors.Where(x => nodeAssignment.Assigned.Contains(x.ConnectorId)).ToArray(),
//                 Revoked   = connectors.Where(x => nodeAssignment.Revoked.Contains(x.ConnectorId)).ToArray(),
//                 Timestamp = DateTimeOffset.UtcNow
//             });
//         });
//
//         RegisteredConnector[] assignedConnectors = [];
//
//         Subscriber.On<ConnectorsAssignment>(async (assignment, _) => {
//             await assignment.Revoked
//                 .Select(connector => DeactivateConnector(connector.ConnectorId))
//                 .WhenAll();
//
//             await assignment.Assigned
//                 .Select(connector => ActivateConnector(connector.ConnectorId, connector.Settings, connector.Revision))
//                 .WhenAll();
//
//             // keep track of assigned Connectors
//             assignedConnectors = assignment.Assigned;
//         });
//
//         Subscriber.On<ConnectorsAssigmentApplied>(async (assignment, _) => {
//             await using var consumer = ConsumerBuilder.StartPosition(connectors.Position).Create();
//
//             await foreach (var record in consumer.Records(stoppingToken)) {
//                 connectors.Connectors.Add(new RegisteredConnector(record.Value.Connector));
//
//                 var newAssignment = Assignor.Assign(clusterTopology, connectors.Select(x => x.Resource), actualAssignment);
//
//                 var nodeAssignment = newAssignment[nodeInfo.InstanceId];
//
//                 Publisher.Publish(new ConnectorsAssignment {
//                     Unchanged = connectors.Where(x => nodeAssignment.Unchanged.Contains(x.ConnectorId)).ToArray(),
//                     Assigned  = connectors.Where(x => nodeAssignment.Assigned.Contains(x.ConnectorId)).ToArray(),
//                     Revoked   = connectors.Where(x => nodeAssignment.Revoked.Contains(x.ConnectorId)).ToArray(),
//                     Timestamp = DateTimeOffset.UtcNow
//                 });
//             }
//         });
//
//         return;
//
//         static IDictionary<string, string?> EnrichWithStartPosition(IDictionary<string, string?> settings, StartFromPosition? startPosition) {
//             if (startPosition is not null)
//                 settings["Subscription:StartPosition"] = startPosition.LogPosition.ToString();
//
//             return settings;
//         }
//
//         async Task ActivateConnector(ConnectorId connectorId, IDictionary<string, string?> settings, int revision) {
//             var activationResult = await Activator.Activate(connectorId, settings, revision, stoppingToken);
//
//             Logger.LogConnectorActivationResult(activationResult.Failure
//                     ? activationResult.Type == ActivateResultType.RevisionAlreadyRunning ? LogLevel.Warning : LogLevel.Error
//                     : LogLevel.Information,
//                 activationResult.Error,
//                 nodeInfo.InstanceId,
//                 connectorId,
//                 activationResult.Type);
//         }
//
//         async Task DeactivateConnector(ConnectorId connectorId) {
//             var deactivationResult = await Activator.Deactivate(connectorId);
//
//             Logger.LogConnectorDeactivationResult(
//                 deactivationResult.Failure
//                     ? deactivationResult.Type == DeactivateResultType.UnableToReleaseLock ? LogLevel.Warning : LogLevel.Error
//                     : LogLevel.Information,
//                 deactivationResult.Error, nodeInfo.InstanceId, connectorId, deactivationResult.Type
//             );
//         }
//     }
// }
//
// // public class ClusterTopologySensor : MessageModule {
// //     public ClusterTopologySensor(ISubscriber subscriber, IPublisher publisher, SystemReadinessProbe systemReadinessProbe, GetActiveConnectors getActiveConnectors, ILogger<ClusterTopologySensor> logger) : base(subscriber) {
// //         CompletionSource = new();
// //
// //         bool systemReady = false;
// //         long counter     = 1;
// //
// //         var lastKnownClusterTopology = ClusterTopology.Unknown;
// //
// //         IConnectorAssignor assignor = new StickyWithAffinityConnectorAssignor();
// //
// //         On<StateChangeMessage>((evt, token) => {
// //             if (evt is BecomeShuttingDown) {
// //                 Drop<GossipMessage.GossipUpdated>();
// //                 Drop<StateChangeMessage>();
// //                 logger.LogWarning("[{ReceivedOrder}] Deactivated", counter++);
// //                 return ValueTask.CompletedTask;
// //             }
// //
// //             if (systemReady || evt is not (BecomeLeader or BecomeFollower or BecomeReadOnlyReplica))
// //                 return ValueTask.CompletedTask;
// //
// //             systemReady = true;
// //
// //             logger.LogWarning("[{ReceivedOrder}] Activated.", counter++);
// //
// //             return ValueTask.CompletedTask;
// //         });
// //
// //         On<GossipMessage.GossipUpdated>(async (evt, token) => {
// //             if (!systemReady) return;
// //
// //             var updatedTopology = ClusterTopology.From(MapToClusterNodes(evt.ClusterInfo.Members));
// //             var topologyChanged = updatedTopology != lastKnownClusterTopology;
// //
// //             if (topologyChanged) {
// //                 lastKnownClusterTopology = updatedTopology;
// //                 logger.LogWarning(
// //                     "[{ReceivedOrder}] Gossip updated: {GossipMembersInfo}",
// //                     counter++, evt.ClusterInfo.Members.Select(x => (x.State, x.InstanceId)));
// //
// //                 publisher.Publish(new ConnectorsControlPlaneMessages.ClusterTopologyChangeDetected(lastKnownClusterTopology, DateTimeOffset.UtcNow));
// //
// //                 //
// //                 // var result = await getActiveConnectors(token);
// //                 //
// //                 // var connectors = result.Select(x => x.Resource);
// //                 //
// //                 // var assignment = assignor.Assign(lastKnownClusterTopology, connectors);
// //             }
// //             else {
// //                 logger.LogWarning(
// //                     "[{ReceivedOrder}] Gossip updated but no topology changes were detected: {GossipMembersInfo}",
// //                     counter++, evt.ClusterInfo.Members.Select(x => (x.State, x.InstanceId)));
// //             }
// //
// //             return;
// //
// //             static ClusterNode[] MapToClusterNodes(MemberInfo[] members) =>
// //                 members
// //                     .Where(x => x.IsAlive)
// //                     .Select(x => new ClusterNode {
// //                         NodeId = x.InstanceId,
// //                         State  = MapToClusterNodeState(x.State)
// //                     })
// //                     .ToArray();
// //
// //             static ClusterNodeState MapToClusterNodeState(VNodeState state) =>
// //                 state switch {
// //                     VNodeState.Leader          => ClusterNodeState.Leader,
// //                     VNodeState.Follower        => ClusterNodeState.Follower,
// //                     VNodeState.ReadOnlyReplica => ClusterNodeState.ReadOnlyReplica,
// //                     _                          => ClusterNodeState.Unmapped
// //                 };
// //         });
// //
// //         On<ConnectorsControlPlaneMessages.ClusterTopologyChangeDetected>(async (evt, token) => {
// //             logger.LogWarning(
// //                 "[{ReceivedOrder}] Topology change detected: {ClusterTopologyInfo}",
// //                 counter++, evt.Topology.Nodes.Select(x => (x.State, x.NodeId)));
// //
// //             // assign and activate connectors!!!
// //         });
// //     }
// //
// //     TaskCompletionSource CompletionSource { get; }
// // }
//
// // public class ClusterTopologySensor :
// //     IAsyncHandle<StateChangeMessage>,
// //     IAsyncHandle<GossipMessage.GossipUpdated>,
// //     IDisposable {
// //     public ClusterTopologySensor(ISubscriber subscriber, SystemReadinessProbe systemReadinessProbe, ILogger<ClusterTopologySensor> logger) {
// //         CompletionSource = new();
// //
// //         Subscriber = subscriber.With(x => {
// //             x.Subscribe<StateChangeMessage>(this);
// //             x.Subscribe<GossipMessage.GossipUpdated>(this);
// //         });
// //
// //         SystemReadinessProbe = systemReadinessProbe;
// //         Logger               = logger;
// //     }
// //
// //     ISubscriber          Subscriber           { get; }
// //     SystemReadinessProbe SystemReadinessProbe { get; }
// //     ILogger              Logger               { get; }
// //     TaskCompletionSource CompletionSource     { get; }
// //
// //     public ValueTask HandleAsync(StateChangeMessage message, CancellationToken cancellationToken) => OnEvent(message, cancellationToken);
// //     public ValueTask HandleAsync(GossipMessage.GossipUpdated message, CancellationToken cancellationToken)      => OnEvent(message, cancellationToken);
// //
// //     long _counter;
// //
// //     async ValueTask OnEvent(object message, CancellationToken cancellationToken) {
// //         var topology = ClusterTopology.Unknown;
// //         if (message is GossipMessage.GossipUpdated gossip)
// //             topology = ClusterTopology.From(MapToClusterNodes(gossip.ClusterInfo.Members));
// //
// //         Logger.LogWarning("[{ReceivedOrder}] RECEIVED EVENT {MessageTypeName}. CLUSTER TOPOLOGY: {ClusterTopologyInfo}",
// //             _counter++,
// //             message.GetType().Name,
// //             topology.Nodes.Select(x => (x.State, x.NodeId))
// //         );
// //
// //         if (message is BecomeShuttingDown) {
// //             Logger.LogWarning("[{ReceivedOrder}] Disabling sensor", _counter++);
// //             Subscriber.Unsubscribe<GossipMessage.GossipUpdated>(this);
// //             Subscriber.Unsubscribe<StateChangeMessage>(this);
// //             return;
// //         }
// //
// //         var systemReady = message is BecomeLeader
// //                                   or BecomeFollower
// //                                   or BecomeReadOnlyReplica;
// //
// //         // we first wait until we are ready: BecomeLeader, BecomeFollower or BecomeReadOnlyReplica
// //         if (!systemReady) return;
// //
// //         Logger.LogWarning("SYSTEM IS READY. CLUSTER TOPOLOGY: {ClusterTopologyInfo}", topology.Nodes.Select(x => (x.State, x.NodeId)));
// //
// //         //
// //         //
// //         // Logger.LogWarning("[{ReceivedOrder}]RECEIVED EVENT {MessageTypeName}. TOPOLOGY: {ClusterTopologyInfo}",
// //         //     _counter++,
// //         //     message.GetType().Name,
// //         //     topology.Nodes.Select(x => (x.State, x.NodeId))
// //         // );
// //
// //         // var sysInfo = await GetNodeSystemInfo();
// //         // Logger.LogWarning("Received event {MessageTypeName}. Node InstanceId: {InstanceId}, Topology: {ClusterTopologyInfo}",
// //         //     message.GetType().Name,
// //         //     sysInfo.InstanceId,
// //         //     topology.Nodes.Select(x => (x.State, x.NodeId))
// //         // );
// //
// //
// //         // var topology = ClusterTopology.From(MapToClusterNodes(message.ClusterInfo.Members));
// //         //
// //         // var (connectors, position) = GetActiveConnectors(CancellationToken.None).GetAwaiter().GetResult();
// //         //
// //         // var result = Assignor.Assign(topology, connectors);
// //
// //         CompletionSource.TrySetResult();
// //
// //         return;
// //
// //         static ClusterNode[] MapToClusterNodes(MemberInfo[] members) =>
// //             members
// //                 .Where(x => x.IsAlive)
// //                 .Select(x => new ClusterNode {
// //                     NodeId = x.InstanceId,
// //                     State  = MapToClusterNodeState(x.State)
// //                 })
// //                 .ToArray();
// //
// //         static ClusterNodeState MapToClusterNodeState(VNodeState state) =>
// //             state switch {
// //                 VNodeState.Leader          => ClusterNodeState.Leader,
// //                 VNodeState.Follower        => ClusterNodeState.Follower,
// //                 VNodeState.ReadOnlyReplica => ClusterNodeState.ReadOnlyReplica,
// //                 _                          => ClusterNodeState.Unmapped
// //             };
// //     }
// //
// //     public async Task<NodeSystemInfo> WaitForChanges(CancellationToken cancellationToken = default) {
// //         var nodeInfo = await SystemReadinessProbe.WaitUntilReady(cancellationToken);
// //
// //         // Subscriber.With(x => {
// //         //     x.Subscribe<SystemMessage.StateChangeMessage>(this);
// //         //     x.Subscribe<GossipMessage.GossipUpdated>(this);
// //         // });
// //
// //
// //
// //         await CompletionSource.Task.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);
// //
// //         return nodeInfo;
// //     }
// //
// //     public void Dispose() {
// //         Subscriber.Unsubscribe<StateChangeMessage>(this);
// //         Subscriber.Unsubscribe<GossipMessage.GossipUpdated>(this);
// //     }
// // }
//
// // public delegate ClusterTopologySensor GetClusterTopologySensor();
// //
// // class ClusterTopologySensorService(GetClusterTopologySensor getSensor) : BackgroundService {
// //     GetClusterTopologySensor GetSensor { get; } = getSensor;
// //
// //     protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
// //         var sensor = GetSensor();
// //     }
// // }
//
// // [UsedImplicitly]
// // public class ClusterTopologySensor :
// //     IAsyncHandle<SystemMessage.BecomeLeader>,
// //     IAsyncHandle<SystemMessage.BecomeFollower>,
// //     IAsyncHandle<SystemMessage.BecomeReadOnlyReplica>,
// //     IAsyncHandle<SystemMessage.BecomeShuttingDown>,
// //     IAsyncHandle<SystemMessage.StateChangeMessage>,
// //     IAsyncHandle<GossipMessage.GossipUpdated>,
// //     IDisposable {
// //     public ClusterTopologySensor(ISubscriber subscriber, GetNodeSystemInfo getNodeSystemInfo, ILogger<ClusterTopologySensor> logger) {
// //         CompletionSource = new();
// //
// //         Subscriber = subscriber.With(x => {
// //             x.Subscribe<SystemMessage.BecomeLeader>(this);
// //             x.Subscribe<SystemMessage.BecomeFollower>(this);
// //             x.Subscribe<SystemMessage.BecomeReadOnlyReplica>(this);
// //             x.Subscribe<SystemMessage.StateChangeMessage>(this);
// //             x.Subscribe<GossipMessage.GossipUpdated>(this);
// //         });
// //
// //         GetNodeSystemInfo = getNodeSystemInfo;
// //         Logger            = logger;
// //     }
// //
// //     ISubscriber          Subscriber        { get; }
// //     GetNodeSystemInfo    GetNodeSystemInfo { get; }
// //     ILogger              Logger            { get; }
// //     TaskCompletionSource CompletionSource  { get; }
// //
// //     public ValueTask HandleAsync(SystemMessage.BecomeLeader message, CancellationToken cancellationToken)          => OnEvent(message, cancellationToken);
// //     public ValueTask HandleAsync(SystemMessage.BecomeFollower message, CancellationToken cancellationToken)        => OnEvent(message, cancellationToken);
// //     public ValueTask HandleAsync(SystemMessage.BecomeReadOnlyReplica message, CancellationToken cancellationToken) => OnEvent(message, cancellationToken);
// //     public ValueTask HandleAsync(SystemMessage.BecomeShuttingDown message, CancellationToken cancellationToken)    => OnEvent(message, cancellationToken);
// //     public ValueTask HandleAsync(SystemMessage.StateChangeMessage message, CancellationToken cancellationToken)    => OnEvent(message, cancellationToken);
// //     public ValueTask HandleAsync(GossipMessage.GossipUpdated message, CancellationToken cancellationToken)         => OnEvent(message, cancellationToken);
// //
// //     long _counter = 0;
// //
// //     async ValueTask OnEvent(object message, CancellationToken cancellationToken) {
// //         var topology = ClusterTopology.Unknown;
// //         if (message is GossipMessage.GossipUpdated gossip)
// //             topology = ClusterTopology.From(MapToClusterNodes(gossip.ClusterInfo.Members));
// //
// //         Logger.LogWarning("[{ReceivedOrder}]RECEIVED EVENT {MessageTypeName}. TOPOLOGY: {ClusterTopologyInfo}",
// //             _counter++,
// //             message.GetType().Name,
// //             topology.Nodes.Select(x => (x.State, x.NodeId))
// //         );
// //
// //         var systemReady = message is SystemMessage.BecomeLeader
// //                                   or SystemMessage.BecomeFollower
// //                                   or SystemMessage.BecomeReadOnlyReplica;
// //
// //         // we first wait until we are ready: BecomeLeader, BecomeFollower or BecomeReadOnlyReplica
// //         if (!systemReady) return;
// //
// //
// //         Logger.LogWarning("[{ReceivedOrder}]RECEIVED EVENT {MessageTypeName}. TOPOLOGY: {ClusterTopologyInfo}",
// //             _counter++,
// //             message.GetType().Name,
// //             topology.Nodes.Select(x => (x.State, x.NodeId))
// //         );
// //
// //         // var sysInfo = await GetNodeSystemInfo();
// //         // Logger.LogWarning("Received event {MessageTypeName}. Node InstanceId: {InstanceId}, Topology: {ClusterTopologyInfo}",
// //         //     message.GetType().Name,
// //         //     sysInfo.InstanceId,
// //         //     topology.Nodes.Select(x => (x.State, x.NodeId))
// //         // );
// //
// //
// //         // var topology = ClusterTopology.From(MapToClusterNodes(message.ClusterInfo.Members));
// //         //
// //         // var (connectors, position) = GetActiveConnectors(CancellationToken.None).GetAwaiter().GetResult();
// //         //
// //         // var result = Assignor.Assign(topology, connectors);
// //
// //         CompletionSource.TrySetResult();
// //
// //         return;
// //
// //         static ClusterNode[] MapToClusterNodes(MemberInfo[] members) =>
// //             members
// //                 .Where(x => x.IsAlive)
// //                 .Select(x => new ClusterNode {
// //                     NodeId = x.InstanceId,
// //                     State  = MapToClusterNodeState(x.State)
// //                 })
// //                 .ToArray();
// //
// //         static ClusterNodeState MapToClusterNodeState(VNodeState state) =>
// //             state switch {
// //                 VNodeState.Leader          => ClusterNodeState.Leader,
// //                 VNodeState.Follower        => ClusterNodeState.Follower,
// //                 VNodeState.ReadOnlyReplica => ClusterNodeState.ReadOnlyReplica,
// //                 _                          => ClusterNodeState.Unmapped
// //             };
// //     }
// //
// //     public async Task<NodeSystemInfo> WaitForChanges(CancellationToken cancellationToken = default) {
// //         await CompletionSource.Task.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);
// //
// //         return await GetNodeSystemInfo();
// //     }
// //
// //     public void Dispose() {
// //         Subscriber.Unsubscribe<SystemMessage.BecomeLeader>(this);
// //         Subscriber.Unsubscribe<SystemMessage.BecomeFollower>(this);
// //         Subscriber.Unsubscribe<SystemMessage.BecomeReadOnlyReplica>(this);
// //         Subscriber.Unsubscribe<SystemMessage.StateChangeMessage>(this);
// //         Subscriber.Unsubscribe<GossipMessage.GossipUpdated>(this);
// //     }
// // }