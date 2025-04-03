// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Storage;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.TransactionLog.Chunks;
using ILogger = Serilog.ILogger;
using OperationResult = EventStore.Core.Messages.OperationResult;

namespace EventStore.Core.Services.VNode;

public abstract class ClusterVNodeController {
	protected static readonly ILogger Log = Serilog.Log.ForContext<ClusterVNodeController>();
	public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
}

public sealed class ClusterVNodeController<TStreamId> : ClusterVNodeController {
	public static readonly TimeSpan LeaderReconnectionDelay = TimeSpan.FromMilliseconds(500);
	private static readonly TimeSpan LeaderSubscriptionRetryDelay = TimeSpan.FromMilliseconds(500);
	private static readonly TimeSpan LeaderSubscriptionTimeout = TimeSpan.FromMilliseconds(1000);
	private static readonly TimeSpan LeaderDiscoveryTimeout = TimeSpan.FromMilliseconds(3000);

	private readonly InMemoryBus _outputBus;
	private readonly VNodeInfo _nodeInfo;
	private readonly TFChunkDb _db;
	private readonly ClusterVNode<TStreamId> _node;
	private readonly INodeStatusTracker _statusTracker;

	private VNodeState _state = VNodeState.Initializing;
	private VNodeState State {
		get => _state;
		set {
			_state = value;
			_statusTracker.OnStateChange(value);
		}
	}

	private MemberInfo _leader;
	private Guid _stateCorrelationId = Guid.NewGuid();
	private Guid _leaderConnectionCorrelationId = Guid.NewGuid();
	private Guid _subscriptionId = Guid.Empty;
	private readonly int _clusterSize;

	private readonly IQueuedHandler _mainQueue;
	private readonly IEnvelope _publishEnvelope;
	private readonly VNodeFSM _fsm;

	private readonly MessageForwardingProxy _forwardingProxy;
	private readonly TimeSpan _forwardingTimeout;
	private readonly int _subsystemCount;
	private readonly Action _startSubsystems;

	private int _subSystemInitsToExpect;

	enum ServiceState {
		Initialized,
		Shutdown,
	};

	private readonly Dictionary<string, ServiceState> _initializedServices = [];
	private bool _publishedSystemStart;

	private bool _exitProcessOnShutdown;

	public ClusterVNodeController(
		QueueStatsManager statsManager,
		Trackers trackers,
		VNodeInfo nodeInfo,
		TFChunkDb db,
		INodeStatusTracker statusTracker,
		ClusterVNodeOptions options, ClusterVNode<TStreamId> node, MessageForwardingProxy forwardingProxy,
		Action startSubsystems) {
		Ensure.NotNull(nodeInfo, "nodeInfo");
		Ensure.NotNull(db, "dbConfig");
		Ensure.NotNull(node, "node");
		Ensure.NotNull(forwardingProxy, "forwardingProxy");
		Ensure.NotNull(startSubsystems, "startSubsystems");

		_nodeInfo = nodeInfo;
		_db = db;
		_node = node;
		_statusTracker = statusTracker;
		_startSubsystems = startSubsystems;
		_subsystemCount = options.Subsystems.Count;
		_subSystemInitsToExpect = _subsystemCount;
		_clusterSize = options.Cluster.ClusterSize;

		_forwardingProxy = forwardingProxy;
		_forwardingTimeout = TimeSpan.FromMilliseconds(options.Database.PrepareTimeoutMs +
													   options.Database.CommitTimeoutMs + 300);

		_outputBus = new InMemoryBus("MainBus");
		_fsm = CreateFSM();
		_mainQueue = new QueuedHandlerThreadPool(_fsm, "MainQueue", statsManager, trackers.QueueTrackers);
		_publishEnvelope = _mainQueue;
	}

	public IPublisher MainQueue => _mainQueue;

	public ISubscriber MainBus => _outputBus;

	private VNodeFSM CreateFSM() {
		var stm = new VNodeFSMBuilder(new(this, in _state))
			.InAnyState()
			.When<SystemMessage.StateChangeMessage>()
				.Do(m => Application.Exit(ExitCode.Error,
					$"{m.GetType().Name} message was unhandled in {GetType().Name}. State: {State}"))
			.When<AuthenticationMessage.AuthenticationProviderInitialized>().Do(Handle)
			.When<AuthenticationMessage.AuthenticationProviderInitializationFailed>().Do(Handle)
			.When<SystemMessage.SubSystemInitialized>().Do(Handle)
			.When<SystemMessage.SystemCoreReady>().Do(Handle)
			.InState(VNodeState.Initializing)
			.When<SystemMessage.SystemInit>().Do(Handle)
			.When<SystemMessage.SystemStart>().Do(Handle)
			.When<SystemMessage.ServiceInitialized>().Do(Handle)
			.When<SystemMessage.BecomeReadOnlyLeaderless>().Do(Handle)
			.When<SystemMessage.BecomeDiscoverLeader>().Do(Handle)
			.When<ClientMessage.ScavengeDatabase>().Ignore()
			.When<ClientMessage.StopDatabaseScavenge>().Ignore()
			.WhenOther().ForwardTo(_outputBus)
			.InStates(VNodeState.DiscoverLeader, VNodeState.Unknown, VNodeState.ReadOnlyLeaderless)
			.WhenOther().ForwardTo(_outputBus)
			.InStates(VNodeState.Initializing, VNodeState.DiscoverLeader, VNodeState.Leader, VNodeState.ResigningLeader, VNodeState.PreLeader,
				VNodeState.PreReplica, VNodeState.CatchingUp, VNodeState.Clone, VNodeState.Follower)
			.When<SystemMessage.BecomeUnknown>().Do(Handle)
			.InAllStatesExcept(VNodeState.DiscoverLeader, VNodeState.Unknown,
				VNodeState.PreReplica, VNodeState.CatchingUp, VNodeState.Clone, VNodeState.Follower,
				VNodeState.PreLeader,
				VNodeState.Leader, VNodeState.ResigningLeader, VNodeState.ReadOnlyLeaderless,
				VNodeState.PreReadOnlyReplica, VNodeState.ReadOnlyReplica)
			.When<ClientMessage.ReadRequestMessage>()
			.Do(msg => DenyRequestBecauseNotReady(msg.Envelope, msg.CorrelationId))
			.InAllStatesExcept(VNodeState.Leader, VNodeState.ResigningLeader,
				VNodeState.PreReplica, VNodeState.CatchingUp, VNodeState.Clone, VNodeState.Follower,
				VNodeState.ReadOnlyReplica, VNodeState.PreReadOnlyReplica)
			.When<ClientMessage.WriteRequestMessage>()
			.Do(msg => DenyRequestBecauseNotReady(msg.Envelope, msg.CorrelationId))
			.InState(VNodeState.Leader)
			.When<ClientMessage.ReadEvent>().ForwardTo(_outputBus)
			.When<ClientMessage.ReadStreamEventsForward>().ForwardTo(_outputBus)
			.When<ClientMessage.ReadStreamEventsBackward>().ForwardTo(_outputBus)
			.When<ClientMessage.ReadAllEventsForward>().ForwardTo(_outputBus)
			.When<ClientMessage.ReadAllEventsBackward>().ForwardTo(_outputBus)
			.When<ClientMessage.FilteredReadAllEventsForward>().ForwardTo(_outputBus)
			.When<ClientMessage.FilteredReadAllEventsBackward>().ForwardTo(_outputBus)
			.When<ClientMessage.WriteEvents>().ForwardTo(_outputBus)
			.When<ClientMessage.TransactionStart>().ForwardTo(_outputBus)
			.When<ClientMessage.TransactionWrite>().ForwardTo(_outputBus)
			.When<ClientMessage.TransactionCommit>().ForwardTo(_outputBus)
			.When<ClientMessage.DeleteStream>().ForwardTo(_outputBus)
			.When<ClientMessage.CreatePersistentSubscriptionToStream>().ForwardTo(_outputBus)
			.When<ClientMessage.ConnectToPersistentSubscriptionToStream>().ForwardTo(_outputBus)
			.When<ClientMessage.UpdatePersistentSubscriptionToStream>().ForwardTo(_outputBus)
			.When<ClientMessage.DeletePersistentSubscriptionToStream>().ForwardTo(_outputBus)
			.When<ClientMessage.CreatePersistentSubscriptionToAll>().ForwardTo(_outputBus)
			.When<ClientMessage.ConnectToPersistentSubscriptionToAll>().ForwardTo(_outputBus)
			.When<ClientMessage.UpdatePersistentSubscriptionToAll>().ForwardTo(_outputBus)
			.When<ClientMessage.DeletePersistentSubscriptionToAll>().ForwardTo(_outputBus)
			.When<SystemMessage.InitiateLeaderResignation>().Do(Handle)
			.When<SystemMessage.BecomeResigningLeader>().Do(Handle)
			.InState(VNodeState.ResigningLeader)
			.When<ClientMessage.ReadEvent>().ForwardTo(_outputBus)
			.When<ClientMessage.ReadStreamEventsForward>().ForwardTo(_outputBus)
			.When<ClientMessage.ReadStreamEventsBackward>().ForwardTo(_outputBus)
			.When<ClientMessage.ReadAllEventsForward>().ForwardTo(_outputBus)
			.When<ClientMessage.ReadAllEventsBackward>().ForwardTo(_outputBus)
			.When<ClientMessage.WriteEvents>().Do(HandleAsResigningLeader)
			.When<ClientMessage.TransactionStart>().Do(HandleAsResigningLeader)
			.When<ClientMessage.TransactionWrite>().Do(HandleAsResigningLeader)
			.When<ClientMessage.TransactionCommit>().Do(HandleAsResigningLeader)
			.When<ClientMessage.DeleteStream>().Do(HandleAsResigningLeader)
			.When<ClientMessage.CreatePersistentSubscriptionToStream>().Do(HandleAsResigningLeader)
			.When<ClientMessage.ConnectToPersistentSubscriptionToStream>().Do(HandleAsResigningLeader)
			.When<ClientMessage.UpdatePersistentSubscriptionToStream>().Do(HandleAsResigningLeader)
			.When<ClientMessage.DeletePersistentSubscriptionToStream>().Do(HandleAsResigningLeader)
			.When<ClientMessage.CreatePersistentSubscriptionToAll>().Do(HandleAsResigningLeader)
			.When<ClientMessage.ConnectToPersistentSubscriptionToAll>().Do(HandleAsResigningLeader)
			.When<ClientMessage.UpdatePersistentSubscriptionToAll>().Do(HandleAsResigningLeader)
			.When<ClientMessage.DeletePersistentSubscriptionToAll>().Do(HandleAsResigningLeader)
			.When<SystemMessage.RequestQueueDrained>().Do(Handle)
			.InAllStatesExcept(VNodeState.ResigningLeader)
			.When<SystemMessage.RequestQueueDrained>().Ignore()
			.InStates(VNodeState.PreReplica, VNodeState.CatchingUp, VNodeState.Clone, VNodeState.Follower,
				VNodeState.DiscoverLeader, VNodeState.Unknown, VNodeState.ReadOnlyLeaderless,
				VNodeState.PreReadOnlyReplica, VNodeState.ReadOnlyReplica)
			.When<ClientMessage.ReadEvent>().Do(HandleAsNonLeader)
			.When<ClientMessage.ReadStreamEventsForward>().Do(HandleAsNonLeader)
			.When<ClientMessage.ReadStreamEventsBackward>().Do(HandleAsNonLeader)
			.When<ClientMessage.ReadAllEventsForward>().Do(HandleAsNonLeader)
			.When<ClientMessage.ReadAllEventsBackward>().Do(HandleAsNonLeader)
			.When<ClientMessage.FilteredReadAllEventsForward>().Do(HandleAsNonLeader)
			.When<ClientMessage.FilteredReadAllEventsBackward>().Do(HandleAsNonLeader)
			.When<ClientMessage.CreatePersistentSubscriptionToStream>().Do(HandleAsNonLeader)
			.When<ClientMessage.ConnectToPersistentSubscriptionToStream>().Do(HandleAsNonLeader)
			.When<ClientMessage.UpdatePersistentSubscriptionToStream>().Do(HandleAsNonLeader)
			.When<ClientMessage.DeletePersistentSubscriptionToStream>().Do(HandleAsNonLeader)
			.When<ClientMessage.CreatePersistentSubscriptionToAll>().Do(HandleAsNonLeader)
			.When<ClientMessage.ConnectToPersistentSubscriptionToAll>().Do(HandleAsNonLeader)
			.When<ClientMessage.UpdatePersistentSubscriptionToAll>().Do(HandleAsNonLeader)
			.When<ClientMessage.DeletePersistentSubscriptionToAll>().Do(HandleAsNonLeader)
			.InStates(VNodeState.ReadOnlyLeaderless, VNodeState.PreReadOnlyReplica, VNodeState.ReadOnlyReplica)
			.When<ClientMessage.WriteEvents>().Do(HandleAsReadOnlyReplica)
			.When<ClientMessage.TransactionStart>().Do(HandleAsReadOnlyReplica)
			.When<ClientMessage.TransactionWrite>().Do(HandleAsReadOnlyReplica)
			.When<ClientMessage.TransactionCommit>().Do(HandleAsReadOnlyReplica)
			.When<ClientMessage.DeleteStream>().Do(HandleAsReadOnlyReplica)
			.When<SystemMessage.VNodeConnectionLost>().Do(HandleAsReadOnlyReplica)
			.When<SystemMessage.BecomePreReadOnlyReplica>().Do(Handle)
			.InStates(VNodeState.PreReplica, VNodeState.CatchingUp, VNodeState.Clone, VNodeState.Follower)
			.When<ClientMessage.WriteEvents>().Do(HandleAsNonLeader)
			.When<ClientMessage.TransactionStart>().Do(HandleAsNonLeader)
			.When<ClientMessage.TransactionWrite>().Do(HandleAsNonLeader)
			.When<ClientMessage.TransactionCommit>().Do(HandleAsNonLeader)
			.When<ClientMessage.DeleteStream>().Do(HandleAsNonLeader)
			.InAnyState()
			.When<ClientMessage.NotHandled>().ForwardTo(_outputBus)
			.When<ClientMessage.ReadEventCompleted>().ForwardTo(_outputBus)
			.When<ClientMessage.ReadStreamEventsForwardCompleted>().ForwardTo(_outputBus)
			.When<ClientMessage.ReadStreamEventsBackwardCompleted>().ForwardTo(_outputBus)
			.When<ClientMessage.ReadAllEventsForwardCompleted>().ForwardTo(_outputBus)
			.When<ClientMessage.ReadAllEventsBackwardCompleted>().ForwardTo(_outputBus)
			.When<ClientMessage.FilteredReadAllEventsForwardCompleted>().ForwardTo(_outputBus)
			.When<ClientMessage.FilteredReadAllEventsBackwardCompleted>().ForwardTo(_outputBus)
			.When<ClientMessage.WriteEventsCompleted>().ForwardTo(_outputBus)
			.When<ClientMessage.TransactionStartCompleted>().ForwardTo(_outputBus)
			.When<ClientMessage.TransactionWriteCompleted>().ForwardTo(_outputBus)
			.When<ClientMessage.TransactionCommitCompleted>().ForwardTo(_outputBus)
			.When<ClientMessage.DeleteStreamCompleted>().ForwardTo(_outputBus)
			.When<SystemMessage.BecomeShuttingDown>().Do(Handle)
			.InAllStatesExcept(VNodeState.Initializing, VNodeState.ShuttingDown, VNodeState.Shutdown,
			VNodeState.ReadOnlyLeaderless, VNodeState.PreReadOnlyReplica, VNodeState.ReadOnlyReplica)
			.When<ElectionMessage.ElectionsDone>().Do(Handle)
			.InStates(VNodeState.DiscoverLeader, VNodeState.Unknown,
				VNodeState.PreReplica, VNodeState.CatchingUp, VNodeState.Clone, VNodeState.Follower,
				VNodeState.PreLeader, VNodeState.Leader)
			.When<SystemMessage.BecomePreReplica>().Do(Handle)
			.When<SystemMessage.BecomePreLeader>().Do(Handle)
			.InStates(VNodeState.PreReplica, VNodeState.CatchingUp, VNodeState.Clone, VNodeState.Follower)
			.When<GossipMessage.GossipUpdated>().Do(HandleAsNonLeader)
			.When<SystemMessage.VNodeConnectionLost>().Do(Handle)
			.InAllStatesExcept(VNodeState.PreReplica, VNodeState.PreLeader, VNodeState.PreReadOnlyReplica)
			.When<SystemMessage.WaitForChaserToCatchUp>().Ignore()
			.When<SystemMessage.ChaserCaughtUp>().Ignore()
			.InStates(VNodeState.PreReplica, VNodeState.PreReadOnlyReplica)
			.When<SystemMessage.BecomeCatchingUp>().Do(Handle)
			.When<SystemMessage.WaitForChaserToCatchUp>().Do(Handle)
			.When<SystemMessage.ChaserCaughtUp>().Do(HandleAsPreReplica)
			.When<ReplicationMessage.ReconnectToLeader>().Do(Handle)
			.When<ReplicationMessage.LeaderConnectionFailed>().Do(Handle)
			.When<ReplicationMessage.SubscribeToLeader>().Do(Handle)
			.When<ReplicationMessage.ReplicaSubscriptionRetry>().Do(Handle)
			.When<ReplicationMessage.ReplicaSubscribed>().Do(Handle)
			.WhenOther().ForwardTo(_outputBus)
			.InAllStatesExcept(VNodeState.PreReplica, VNodeState.PreReadOnlyReplica)
			.When<ReplicationMessage.ReconnectToLeader>().Ignore()
			.When<ReplicationMessage.LeaderConnectionFailed>().Ignore()
			.When<ReplicationMessage.SubscribeToLeader>().Ignore()
			.When<ReplicationMessage.ReplicaSubscriptionRetry>().Ignore()
			.When<ReplicationMessage.ReplicaSubscribed>().Ignore()
			.InStates(VNodeState.CatchingUp, VNodeState.Clone, VNodeState.Follower, VNodeState.ReadOnlyReplica)
			.When<ReplicationMessage.CreateChunk>().Do(ForwardReplicationMessage)
			.When<ReplicationMessage.RawChunkBulk>().Do(ForwardReplicationMessage)
			.When<ReplicationMessage.DataChunkBulk>().Do(ForwardReplicationMessage)
			.When<ReplicationMessage.AckLogPosition>().ForwardTo(_outputBus)
			.WhenOther().ForwardTo(_outputBus)
			.InAllStatesExcept(VNodeState.CatchingUp, VNodeState.Clone, VNodeState.Follower, VNodeState.ReadOnlyReplica)
			.When<ReplicationMessage.CreateChunk>().Ignore()
			.When<ReplicationMessage.RawChunkBulk>().Ignore()
			.When<ReplicationMessage.DataChunkBulk>().Ignore()
			.When<ReplicationMessage.AckLogPosition>().Ignore()
			.InState(VNodeState.CatchingUp)
			.When<ReplicationMessage.CloneAssignment>().Do(Handle)
			.When<ReplicationMessage.FollowerAssignment>().Do(Handle)
			.When<SystemMessage.BecomeClone>().Do(Handle)
			.When<SystemMessage.BecomeFollower>().Do(Handle)
			.InState(VNodeState.Clone)
			.When<ReplicationMessage.DropSubscription>().Do(Handle)
			.When<ReplicationMessage.FollowerAssignment>().Do(Handle)
			.When<SystemMessage.BecomeFollower>().Do(Handle)
			.InState(VNodeState.Follower)
			.When<ReplicationMessage.CloneAssignment>().Do(Handle)
			.When<SystemMessage.BecomeClone>().Do(Handle)
			.InStates(VNodeState.PreReadOnlyReplica, VNodeState.ReadOnlyReplica)
			.When<GossipMessage.GossipUpdated>().Do(HandleAsReadOnlyReplica)
			.When<SystemMessage.BecomeReadOnlyLeaderless>().Do(Handle)
			.InStates(VNodeState.ReadOnlyLeaderless)
			.When<GossipMessage.GossipUpdated>().Do(HandleAsReadOnlyLeaderLess)
			.InState(VNodeState.PreReadOnlyReplica)
			.When<SystemMessage.BecomeReadOnlyReplica>().Do(Handle)
			.InStates(VNodeState.PreLeader, VNodeState.Leader, VNodeState.ResigningLeader)
			.When<SystemMessage.NoQuorumMessage>().Do(Handle)
			.When<GossipMessage.GossipUpdated>().Do(HandleAsLeader)
			.When<ReplicationMessage.ReplicaSubscriptionRequest>().ForwardTo(_outputBus)
			.When<ReplicationMessage.ReplicaLogPositionAck>().ForwardTo(_outputBus)
			.InAllStatesExcept(VNodeState.PreLeader, VNodeState.Leader, VNodeState.ResigningLeader)
			.When<SystemMessage.NoQuorumMessage>().Ignore()
			.When<ReplicationMessage.ReplicaSubscriptionRequest>().Ignore()
			.InState(VNodeState.PreLeader)
			.When<SystemMessage.BecomeLeader>().Do(Handle)
			.When<SystemMessage.WaitForChaserToCatchUp>().Do(Handle)
			.When<SystemMessage.ChaserCaughtUp>().Do(HandleAsPreLeader)
			.WhenOther().ForwardTo(_outputBus)
			.InStates(VNodeState.Leader, VNodeState.ResigningLeader)
			.When<StorageMessage.WritePrepares>().ForwardTo(_outputBus)
			.When<StorageMessage.WriteDelete>().ForwardTo(_outputBus)
			.When<StorageMessage.WriteTransactionStart>().ForwardTo(_outputBus)
			.When<StorageMessage.WriteTransactionData>().ForwardTo(_outputBus)
			.When<StorageMessage.WriteTransactionEnd>().ForwardTo(_outputBus)
			.When<StorageMessage.WriteCommit>().ForwardTo(_outputBus)
			.WhenOther().ForwardTo(_outputBus)
			.InAllStatesExcept(VNodeState.Leader, VNodeState.ResigningLeader)
			.When<SystemMessage.InitiateLeaderResignation>().Ignore()
			.When<SystemMessage.BecomeResigningLeader>().Ignore()
			.When<StorageMessage.WritePrepares>().Ignore()
			.When<StorageMessage.WriteDelete>().Ignore()
			.When<StorageMessage.WriteTransactionStart>().Ignore()
			.When<StorageMessage.WriteTransactionData>().Ignore()
			.When<StorageMessage.WriteTransactionEnd>().Ignore()
			.When<StorageMessage.WriteCommit>().Ignore()
			.InState(VNodeState.ShuttingDown)
			.When<SystemMessage.BecomeShutdown>().Do(Handle)
			.When<SystemMessage.ShutdownTimeout>().Do(Handle)
			.InStates(VNodeState.ShuttingDown, VNodeState.Shutdown)
			.When<SystemMessage.ServiceShutdown>().Do(Handle)
			.WhenOther().ForwardTo(_outputBus)
			.InState(VNodeState.DiscoverLeader)
			.When<GossipMessage.GossipUpdated>().Do(HandleAsDiscoverLeader)
			.When<LeaderDiscoveryMessage.DiscoveryTimeout>().Do(HandleAsDiscoverLeader)
			.Build();
		return stm;
	}

	public void Start() => _mainQueue.Start();

	private ValueTask Handle(SystemMessage.SystemInit message, CancellationToken token) {
		Log.Information("========== [{httpEndPoint}] SYSTEM INIT...", _nodeInfo.HttpEndPoint);
		return _outputBus.DispatchAsync(message, token);
	}

	private async ValueTask Handle(SystemMessage.SystemStart message, CancellationToken token) {
		Log.Information("========== [{httpEndPoint}] SYSTEM START...", _nodeInfo.HttpEndPoint);
		await _outputBus.DispatchAsync(message, token);

		var id = Guid.NewGuid();
		Message msg = _nodeInfo.IsReadOnlyReplica
			? new SystemMessage.BecomeReadOnlyLeaderless(id)
			: _clusterSize > 1
				? new SystemMessage.BecomeDiscoverLeader(id)
				: new SystemMessage.BecomeUnknown(id);

		await _fsm.HandleAsync(msg, token);
	}

	private async ValueTask Handle(SystemMessage.BecomeUnknown message, CancellationToken token) {
		Log.Information("========== [{httpEndPoint}] IS UNKNOWN...", _nodeInfo.HttpEndPoint);

		State = VNodeState.Unknown;
		_leader = null;
		await _outputBus.DispatchAsync(message, token);
		_mainQueue.Publish(new ElectionMessage.StartElections());
	}

	private async ValueTask Handle(SystemMessage.BecomeDiscoverLeader message, CancellationToken token) {
		Log.Information("========== [{httpEndPoint}] IS ATTEMPTING TO DISCOVER EXISTING LEADER...", _nodeInfo.HttpEndPoint);

		State = VNodeState.DiscoverLeader;
		await _outputBus.DispatchAsync(message, token);

		var msg = new LeaderDiscoveryMessage.DiscoveryTimeout();
		_mainQueue.Publish(TimerMessage.Schedule.Create(LeaderDiscoveryTimeout, _publishEnvelope, msg));
	}

	private ValueTask Handle(SystemMessage.InitiateLeaderResignation message, CancellationToken token) {
		Log.Information("========== [{httpEndPoint}] IS INITIATING LEADER RESIGNATION...", _nodeInfo.HttpEndPoint);

		return _fsm.HandleAsync(new SystemMessage.BecomeResigningLeader(_stateCorrelationId), token);
	}

	private ValueTask Handle(SystemMessage.BecomeResigningLeader message, CancellationToken token) {
		Log.Information("========== [{httpEndPoint}] IS RESIGNING LEADER...", _nodeInfo.HttpEndPoint);
		if (_stateCorrelationId != message.CorrelationId)
			return ValueTask.CompletedTask;

		State = VNodeState.ResigningLeader;
		return _outputBus.DispatchAsync(message, token);
	}

	private ValueTask Handle(SystemMessage.RequestQueueDrained message, CancellationToken token) {
		Log.Information("========== [{httpEndPoint}] REQUEST QUEUE DRAINED. RESIGNATION COMPLETE.", _nodeInfo.HttpEndPoint);
		return _fsm.HandleAsync(new SystemMessage.BecomeUnknown(Guid.NewGuid()), token);
	}

	private async ValueTask Handle(SystemMessage.BecomePreReplica message, CancellationToken token) {
		if (_leader is null)
			throw new Exception("_leader == null");
		if (_stateCorrelationId != message.CorrelationId)
			return;

		Log.Information(
			"========== [{httpEndPoint}] PRE-REPLICA STATE, WAITING FOR CHASER TO CATCH UP... LEADER IS [{masterHttp},{masterId:B}]",
			_nodeInfo.HttpEndPoint, _leader.HttpEndPoint, _leader.InstanceId);

		State = VNodeState.PreReplica;
		await _outputBus.DispatchAsync(message, token);
		_mainQueue.Publish(new SystemMessage.WaitForChaserToCatchUp(_stateCorrelationId, TimeSpan.Zero));
	}

	private async ValueTask Handle(SystemMessage.BecomePreReadOnlyReplica message, CancellationToken token) {
		if (_stateCorrelationId != message.CorrelationId)
			return;

		if (_leader is null)
			throw new Exception("_leader == null");

		Log.Information(
			"========== [{httpEndPoint}] READ ONLY PRE-REPLICA STATE, WAITING FOR CHASER TO CATCH UP... LEADER IS [{leaderHttp},{leaderId:B}]",
			_nodeInfo.HttpEndPoint, _leader.HttpEndPoint, _leader.InstanceId);

		State = VNodeState.PreReadOnlyReplica;
		await _outputBus.DispatchAsync(message, token);
		_mainQueue.Publish(new SystemMessage.WaitForChaserToCatchUp(_stateCorrelationId, TimeSpan.Zero));
	}

	private ValueTask Handle(SystemMessage.BecomeCatchingUp message, CancellationToken token) {
		if (_leader is null)
			return ValueTask.FromException(new Exception("_leader == null"));

		if (_stateCorrelationId != message.CorrelationId)
			return ValueTask.CompletedTask;

		Log.Information("========== [{httpEndPoint}] IS CATCHING UP... LEADER IS [{leaderHttp},{leaderId:B}]",
			_nodeInfo.HttpEndPoint, _leader.HttpEndPoint, _leader.InstanceId);

		State = VNodeState.CatchingUp;
		return _outputBus.DispatchAsync(message, token);
	}

	private ValueTask Handle(SystemMessage.BecomeClone message, CancellationToken token) {
		if (_leader is null)
			return ValueTask.FromException(new Exception("_leader == null"));

		if (_stateCorrelationId != message.CorrelationId)
			return ValueTask.CompletedTask;

		Log.Information("========== [{httpEndPoint}] IS CLONE... LEADER IS [{leaderHttp},{leaderId:B}]",
			_nodeInfo.HttpEndPoint, _leader.HttpEndPoint, _leader.InstanceId);

		State = VNodeState.Clone;
		return _outputBus.DispatchAsync(message, token);
	}

	private ValueTask Handle(SystemMessage.BecomeFollower message, CancellationToken token) {
		if (_leader is null)
			return ValueTask.FromException(new Exception("_leader == null"));

		if (_stateCorrelationId != message.CorrelationId)
			return ValueTask.CompletedTask;

		Log.Information("========== [{httpEndPoint}] IS FOLLOWER... LEADER IS [{leaderHttp},{leaderId:B}]",
			_nodeInfo.HttpEndPoint, _leader.HttpEndPoint, _leader.InstanceId);

		State = VNodeState.Follower;
		return _outputBus.DispatchAsync(message, token);
	}

	private ValueTask Handle(SystemMessage.BecomeReadOnlyLeaderless message, CancellationToken token) {
		Log.Information("========== [{httpEndPoint}] IS READ ONLY REPLICA WITH UNKNOWN LEADER...", _nodeInfo.HttpEndPoint);

		State = VNodeState.ReadOnlyLeaderless;
		_leader = null;
		return _outputBus.DispatchAsync(message, token);
	}

	private ValueTask Handle(SystemMessage.BecomeReadOnlyReplica message, CancellationToken token) {
		if (_leader is null)
			return ValueTask.FromException(new Exception("_leader == null"));

		if (_stateCorrelationId != message.CorrelationId)
			return ValueTask.CompletedTask;

		Log.Information("========== [{httpEndPoint}] IS READ ONLY REPLICA... LEADER IS [{leaderHttp},{leaderId:B}]",
			_nodeInfo.HttpEndPoint, _leader.HttpEndPoint, _leader.InstanceId);

		State = VNodeState.ReadOnlyReplica;
		return _outputBus.DispatchAsync(message, token);
	}

	private async ValueTask Handle(SystemMessage.BecomePreLeader message, CancellationToken token) {
		if (_leader is null)
			throw new Exception("_leader == null");
		if (_stateCorrelationId != message.CorrelationId)
			return;

		Log.Information("========== [{httpEndPoint}] PRE-LEADER STATE, WAITING FOR CHASER TO CATCH UP...",
			_nodeInfo.HttpEndPoint);

		State = VNodeState.PreLeader;
		await _outputBus.DispatchAsync(message, token);
		_mainQueue.Publish(new SystemMessage.WaitForChaserToCatchUp(_stateCorrelationId, TimeSpan.Zero));
	}

	private ValueTask Handle(SystemMessage.BecomeLeader message, CancellationToken token) {
		if (State is VNodeState.Leader)
			return ValueTask.FromException(new Exception("We should not BecomeLeader twice in a row."));

		if (_leader is null)
			return ValueTask.FromException(new Exception("_leader == null"));

		if (_stateCorrelationId != message.CorrelationId)
			return ValueTask.CompletedTask;

		Log.Information("========== [{httpEndPoint}] IS LEADER... SPARTA!", _nodeInfo.HttpEndPoint);

		State = VNodeState.Leader;
		return _outputBus.DispatchAsync(message, token);
	}

	private ValueTask Handle(SystemMessage.BecomeShuttingDown message, CancellationToken token) {
		if (State is VNodeState.ShuttingDown or VNodeState.Shutdown)
			return ValueTask.CompletedTask;

		Log.Information("========== [{httpEndPoint}] IS SHUTTING DOWN...", _nodeInfo.HttpEndPoint);
		_leader = null;
		_stateCorrelationId = message.CorrelationId;
		_exitProcessOnShutdown = message.ExitProcess;
		State = VNodeState.ShuttingDown;
		_mainQueue.Publish(TimerMessage.Schedule.Create(ShutdownTimeout, _publishEnvelope,
			new SystemMessage.ShutdownTimeout()));
		return _outputBus.DispatchAsync(message, token);
	}

	private async ValueTask Handle(SystemMessage.BecomeShutdown message, CancellationToken token) {
		Log.Information("========== [{httpEndPoint}] IS SHUT DOWN.", _nodeInfo.HttpEndPoint);
		State = VNodeState.Shutdown;
		try {
			await _outputBus.DispatchAsync(message, token);
		} catch (Exception exc) {
			Log.Error(exc, "Error when publishing {message}.", message);
		}

		try {
			await _node.WorkersHandler.Stop();
			_mainQueue.RequestStop();
		} catch (Exception exc) {
			Log.Error(exc, "Error when stopping workers/main queue.");
		}

		if (_exitProcessOnShutdown) {
			Application.Exit(ExitCode.Success, "Shutdown and exit from process was requested.");
		}
	}

	private async ValueTask Handle(ElectionMessage.ElectionsDone message, CancellationToken token) {
		if (_leader != null && _leader.InstanceId == message.Leader.InstanceId) {
			//if the leader hasn't changed, we skip state changes through PreLeader or PreReplica
			if (_leader.InstanceId == _nodeInfo.InstanceId && State == VNodeState.Leader) {
				//transitioning from leader to leader, we just write a new epoch
				await _fsm.HandleAsync(new SystemMessage.WriteEpoch(message.ProposalNumber), token);
			}

			return;
		}

		_leader = message.Leader;
		_subscriptionId = Guid.NewGuid();
		_stateCorrelationId = Guid.NewGuid();
		_leaderConnectionCorrelationId = Guid.NewGuid();
		await _outputBus.DispatchAsync(message, token);

		Message msg = _leader.InstanceId == _nodeInfo.InstanceId
			? new SystemMessage.BecomePreLeader(_stateCorrelationId)
			: new SystemMessage.BecomePreReplica(_stateCorrelationId, _leaderConnectionCorrelationId, _leader);

		await _fsm.HandleAsync(msg, token);
	}

	private async ValueTask Handle(SystemMessage.ServiceInitialized message, CancellationToken token) {
		Log.Information("========== [{httpEndPoint}] Service '{service}' initialized.", _nodeInfo.HttpEndPoint,
			message.ServiceName);

		_initializedServices[message.ServiceName] = ServiceState.Initialized;

		await _outputBus.DispatchAsync(message, token);

		if (!_publishedSystemStart) {
			// may be interested in these three because they facilitate the initialization of the authentication provider
			var trioStarted =
				_initializedServices.TryGetValue(nameof(StorageChaser), out var x) && x is ServiceState.Initialized &&
				_initializedServices.TryGetValue(nameof(StorageReaderService), out var y) && y is ServiceState.Initialized &&
				_initializedServices.TryGetValue(nameof(StorageWriterService), out var z) && z is ServiceState.Initialized;

			if (trioStarted) {
				_mainQueue.Publish(new SystemMessage.SystemStart());
				_publishedSystemStart = true;
			}
		}
	}

	private async ValueTask Handle(AuthenticationMessage.AuthenticationProviderInitialized message,
		CancellationToken token) {
		_startSubsystems();
		await _outputBus.DispatchAsync(message, token);
		await _fsm.HandleAsync(new SystemMessage.SystemCoreReady(), token);
	}

	private ValueTask Handle(AuthenticationMessage.AuthenticationProviderInitializationFailed message, CancellationToken token) {
		Log.Error("Authentication Provider Initialization Failed. Shutting Down.");
		return _fsm.HandleAsync(new SystemMessage.BecomeShutdown(Guid.NewGuid()), token);
	}

	private ValueTask Handle(SystemMessage.SystemCoreReady message, CancellationToken token) {
		Message msg = _subsystemCount is 0
			? new SystemMessage.SystemReady()
			: message;

		return _outputBus.DispatchAsync(msg, token);
	}

	private ValueTask Handle(SystemMessage.SubSystemInitialized message, CancellationToken token) {
		Log.Information("========== [{httpEndPoint}] Sub System '{subSystemName}' initialized.", _nodeInfo.HttpEndPoint,
			message.SubSystemName);

		return Interlocked.Decrement(ref _subSystemInitsToExpect) is 0
			? _outputBus.DispatchAsync(new SystemMessage.SystemReady(), token)
			: ValueTask.CompletedTask;
	}

	private void HandleAsResigningLeader(ClientMessage.WriteEvents message) {
		DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
	}

	private void HandleAsResigningLeader(ClientMessage.TransactionStart message) {
		DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
	}
	private void HandleAsResigningLeader(ClientMessage.TransactionWrite message) {
		DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
	}
	private void HandleAsResigningLeader(ClientMessage.TransactionCommit message) {
		DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
	}
	private void HandleAsResigningLeader(ClientMessage.DeleteStream message) {
		DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
	}
	private void HandleAsResigningLeader(ClientMessage.CreatePersistentSubscriptionToStream message) {
		DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
	}
	private void HandleAsResigningLeader(ClientMessage.ConnectToPersistentSubscriptionToStream message) {
		DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
	}
	private void HandleAsResigningLeader(ClientMessage.UpdatePersistentSubscriptionToStream message) {
		DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
	}
	private void HandleAsResigningLeader(ClientMessage.DeletePersistentSubscriptionToStream message) {
		DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
	}
	private void HandleAsResigningLeader(ClientMessage.CreatePersistentSubscriptionToAll message) {
		DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
	}
	private void HandleAsResigningLeader(ClientMessage.ConnectToPersistentSubscriptionToAll message) {
		DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
	}
	private void HandleAsResigningLeader(ClientMessage.UpdatePersistentSubscriptionToAll message) {
		DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
	}
	private void HandleAsResigningLeader(ClientMessage.DeletePersistentSubscriptionToAll message) {
		DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
	}

	private ValueTask HandleAsNonLeader(ClientMessage.ReadEvent message, CancellationToken token) {
		if (!message.RequireLeader)
			return _outputBus.DispatchAsync(message, token);

		if (_leader is null)
			DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
		else
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);

		return ValueTask.CompletedTask;
	}

	private ValueTask HandleAsNonLeader(ClientMessage.ReadStreamEventsForward message, CancellationToken token) {
		if (!message.RequireLeader)
			return _outputBus.DispatchAsync(message, token);

		if (_leader is null)
			DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
		else
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);

		return ValueTask.CompletedTask;
	}

	private ValueTask HandleAsNonLeader(ClientMessage.ReadStreamEventsBackward message, CancellationToken token) {
		if (!message.RequireLeader)
			return _outputBus.DispatchAsync(message, token);

		if (_leader is null)
			DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
		else
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);

		return ValueTask.CompletedTask;
	}

	private ValueTask HandleAsNonLeader(ClientMessage.ReadAllEventsForward message, CancellationToken token) {
		if (!message.RequireLeader)
			return _outputBus.DispatchAsync(message, token);

		if (_leader is null)
			DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
		else
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);

		return ValueTask.CompletedTask;
	}

	private ValueTask HandleAsNonLeader(ClientMessage.FilteredReadAllEventsForward message, CancellationToken token) {
		if (!message.RequireLeader)
			return _outputBus.DispatchAsync(message, token);

		if (_leader is null)
			DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
		else
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);

		return ValueTask.CompletedTask;
	}

	private ValueTask HandleAsNonLeader(ClientMessage.ReadAllEventsBackward message, CancellationToken token) {
		if (!message.RequireLeader)
			return _outputBus.DispatchAsync(message, token);

		if (_leader is null)
			DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
		else
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);

		return ValueTask.CompletedTask;
	}

	private ValueTask HandleAsNonLeader(ClientMessage.FilteredReadAllEventsBackward message, CancellationToken token) {
		if (!message.RequireLeader)
			return _outputBus.DispatchAsync(message, token);

		if (_leader is null)
			DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
		else
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);

		return ValueTask.CompletedTask;
	}

	private void HandleAsNonLeader(ClientMessage.CreatePersistentSubscriptionToStream message) {
		if (_leader is null)
			DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
		else
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
	}

	private void HandleAsNonLeader(ClientMessage.ConnectToPersistentSubscriptionToStream message) {
		if (_leader is null)
			DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
		else
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
	}

	private void HandleAsNonLeader(ClientMessage.UpdatePersistentSubscriptionToStream message) {
		if (_leader is null)
			DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
		else
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
	}

	private void HandleAsNonLeader(ClientMessage.DeletePersistentSubscriptionToStream message) {
		if (_leader is null)
			DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
		else
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
	}

	private void HandleAsNonLeader(ClientMessage.CreatePersistentSubscriptionToAll message) {
		if (_leader is null)
			DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
		else
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
	}

	private void HandleAsNonLeader(ClientMessage.ConnectToPersistentSubscriptionToAll message) {
		if (_leader is null)
			DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
		else
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
	}

	private void HandleAsNonLeader(ClientMessage.UpdatePersistentSubscriptionToAll message) {
		if (_leader is null)
			DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
		else
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
	}

	private void HandleAsNonLeader(ClientMessage.DeletePersistentSubscriptionToAll message) {
		if (_leader is null)
			DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
		else
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
	}

	private ValueTask HandleAsNonLeader(ClientMessage.WriteEvents message, CancellationToken token) {
		if (message.RequireLeader) {
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
			return ValueTask.CompletedTask;
		}

		var timeoutMessage = new ClientMessage.WriteEventsCompleted(
			message.CorrelationId, OperationResult.ForwardTimeout, "Forwarding timeout");
		return ForwardRequest(message, timeoutMessage, token);
	}

	private ValueTask HandleAsNonLeader(ClientMessage.TransactionStart message, CancellationToken token) {
		if (message.RequireLeader) {
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
			return ValueTask.CompletedTask;
		}

		var timeoutMessage = new ClientMessage.TransactionStartCompleted(
			message.CorrelationId, -1, OperationResult.ForwardTimeout, "Forwarding timeout");
		return ForwardRequest(message, timeoutMessage, token);
	}

	private ValueTask HandleAsNonLeader(ClientMessage.TransactionWrite message, CancellationToken token) {
		if (message.RequireLeader) {
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
			return ValueTask.CompletedTask;
		}

		var timeoutMessage = new ClientMessage.TransactionWriteCompleted(
			message.CorrelationId, message.TransactionId, OperationResult.ForwardTimeout, "Forwarding timeout");
		return ForwardRequest(message, timeoutMessage, token);
	}

	private ValueTask HandleAsNonLeader(ClientMessage.TransactionCommit message, CancellationToken token) {
		if (message.RequireLeader) {
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
			return ValueTask.CompletedTask;
		}

		var timeoutMessage = new ClientMessage.TransactionCommitCompleted(
			message.CorrelationId, message.TransactionId, OperationResult.ForwardTimeout, "Forwarding timeout");
		return ForwardRequest(message, timeoutMessage, token);
	}

	private ValueTask HandleAsNonLeader(ClientMessage.DeleteStream message, CancellationToken token) {
		if (message.RequireLeader) {
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
			return ValueTask.CompletedTask;
		}

		var timeoutMessage = new ClientMessage.DeleteStreamCompleted(
			message.CorrelationId, OperationResult.ForwardTimeout, "Forwarding timeout", -1, -1, -1);
		return ForwardRequest(message, timeoutMessage, token);
	}

	private ValueTask ForwardRequest(ClientMessage.WriteRequestMessage msg, Message timeoutMessage, CancellationToken token) {
		try {
			_forwardingProxy.Register(msg.InternalCorrId, msg.CorrelationId, msg.Envelope, _forwardingTimeout,
				timeoutMessage);
		} catch (Exception ex) {
			return ValueTask.FromException(ex);
		}

		return _outputBus.DispatchAsync(new ClientMessage.TcpForwardMessage(msg), token);
	}

	private void DenyRequestBecauseNotLeader(Guid correlationId, IEnvelope envelope) {
		LeaderInfoProvider leaderInfoProvider = new LeaderInfoProvider(_node.GossipAdvertiseInfo, _leader);
		var endpoints = leaderInfoProvider.GetLeaderInfoEndPoints();
		envelope.ReplyWith(
			new ClientMessage.NotHandled(correlationId,
				ClientMessage.NotHandled.Types.NotHandledReason.NotLeader,
				new ClientMessage.NotHandled.Types.LeaderInfo(endpoints.AdvertisedTcpEndPoint,
					endpoints.IsTcpEndPointSecure,
					endpoints.AdvertisedHttpEndPoint
					)));
	}

	private ValueTask HandleAsReadOnlyReplica(ClientMessage.WriteEvents message, CancellationToken token) {
		if (message.RequireLeader) {
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
			return ValueTask.CompletedTask;
		}
		if (message.User != SystemAccounts.System) {
			DenyRequestBecauseReadOnly(message.CorrelationId, message.Envelope);
			return ValueTask.CompletedTask;
		}

		var timeoutMessage = new ClientMessage.WriteEventsCompleted(
			message.CorrelationId, OperationResult.ForwardTimeout, "Forwarding timeout");
		return ForwardRequest(message, timeoutMessage, token);
	}

	private ValueTask HandleAsReadOnlyReplica(ClientMessage.TransactionStart message, CancellationToken token) {
		var task = ValueTask.CompletedTask;
		if (message.RequireLeader) {
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
		} else if (message.User != SystemAccounts.System) {
			DenyRequestBecauseReadOnly(message.CorrelationId, message.Envelope);
		} else {
			var timeoutMessage = new ClientMessage.TransactionStartCompleted(
				message.CorrelationId, -1, OperationResult.ForwardTimeout, "Forwarding timeout");
			task = ForwardRequest(message, timeoutMessage, token);
		}

		return task;
	}

	private ValueTask HandleAsReadOnlyReplica(ClientMessage.TransactionWrite message, CancellationToken token) {
		var task = ValueTask.CompletedTask;
		if (message.RequireLeader) {
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
		} else if (message.User != SystemAccounts.System) {
			DenyRequestBecauseReadOnly(message.CorrelationId, message.Envelope);
		} else {
			var timeoutMessage = new ClientMessage.TransactionWriteCompleted(
				message.CorrelationId, message.TransactionId, OperationResult.ForwardTimeout, "Forwarding timeout");
			task = ForwardRequest(message, timeoutMessage, token);
		}

		return task;
	}

	private ValueTask HandleAsReadOnlyReplica(ClientMessage.TransactionCommit message, CancellationToken token) {
		var task = ValueTask.CompletedTask;
		if (message.RequireLeader) {
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
		} else if (message.User != SystemAccounts.System) {
			DenyRequestBecauseReadOnly(message.CorrelationId, message.Envelope);
		} else {
			var timeoutMessage = new ClientMessage.TransactionCommitCompleted(
				message.CorrelationId, message.TransactionId, OperationResult.ForwardTimeout, "Forwarding timeout");
			task = ForwardRequest(message, timeoutMessage, token);
		}

		return task;
	}

	private ValueTask HandleAsReadOnlyReplica(ClientMessage.DeleteStream message, CancellationToken token) {
		var task = ValueTask.CompletedTask;
		if (message.RequireLeader) {
			DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
		} else if (message.User != SystemAccounts.System) {
			DenyRequestBecauseReadOnly(message.CorrelationId, message.Envelope);
		} else {
			var timeoutMessage = new ClientMessage.DeleteStreamCompleted(
				message.CorrelationId, OperationResult.ForwardTimeout, "Forwarding timeout", -1, -1, -1);
			task = ForwardRequest(message, timeoutMessage, token);
		}

		return task;
	}

	private void DenyRequestBecauseReadOnly(Guid correlationId, IEnvelope envelope) {
		LeaderInfoProvider leaderInfoProvider = new LeaderInfoProvider(_node.GossipAdvertiseInfo, _leader);
		var endpoints = leaderInfoProvider.GetLeaderInfoEndPoints();
		envelope.ReplyWith(
			new ClientMessage.NotHandled(correlationId,
				ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly,
				new ClientMessage.NotHandled.Types.LeaderInfo(endpoints.AdvertisedTcpEndPoint,
					endpoints.IsTcpEndPointSecure,
					endpoints.AdvertisedHttpEndPoint
					)));
	}

	private void DenyRequestBecauseNotReady(IEnvelope envelope, Guid correlationId) {
		envelope.ReplyWith(new ClientMessage.NotHandled(correlationId,
			ClientMessage.NotHandled.Types.NotHandledReason.NotReady, ((string)null)));
	}

	private ValueTask Handle(SystemMessage.VNodeConnectionLost message, CancellationToken token) {
		if (_leader?.Is(message.VNodeEndPoint) ?? false) // leader connection failed
		{
			_leaderConnectionCorrelationId = Guid.NewGuid();
			var msg = State is VNodeState.PreReplica
				? (Message)new ReplicationMessage.ReconnectToLeader(_leaderConnectionCorrelationId, _leader)
				: new SystemMessage.BecomePreReplica(_stateCorrelationId, _leaderConnectionCorrelationId, _leader);
			_mainQueue.Publish(TimerMessage.Schedule.Create(LeaderReconnectionDelay, _publishEnvelope, msg));
		}

		return _outputBus.DispatchAsync(message, token);
	}

	private ValueTask HandleAsReadOnlyReplica(SystemMessage.VNodeConnectionLost message, CancellationToken token) {
		if (_leader?.Is(message.VNodeEndPoint) ?? false) // leader connection failed
		{
			_leaderConnectionCorrelationId = Guid.NewGuid();
			var msg = State is VNodeState.PreReadOnlyReplica
				? (Message)new ReplicationMessage.ReconnectToLeader(_leaderConnectionCorrelationId, _leader)
				: new SystemMessage.BecomePreReadOnlyReplica(_stateCorrelationId, _leaderConnectionCorrelationId, _leader);
			_mainQueue.Publish(TimerMessage.Schedule.Create(LeaderReconnectionDelay, _publishEnvelope, msg));
		}

		return _outputBus.DispatchAsync(message, token);
	}

	private ValueTask HandleAsLeader(GossipMessage.GossipUpdated message, CancellationToken token) {
		if (_leader is null)
			return ValueTask.FromException(new Exception("_leader == null"));

		if (message.ClusterInfo.Members.Count(IsAliveLeader) > 1) {
			Log.Debug("There are MULTIPLE LEADERS according to gossip, need to start elections. LEADER: [{leader}]",
				_leader);
			Log.Debug("GOSSIP:");
			Log.Debug("{clusterInfo}", message.ClusterInfo);
			_mainQueue.Publish(new ElectionMessage.StartElections());
		}

		return _outputBus.DispatchAsync(message, token);
	}

	private async ValueTask HandleAsReadOnlyReplica(GossipMessage.GossipUpdated message, CancellationToken token) {
		if (_leader is null)
			throw new Exception("_leader == null");

		var aliveLeaders = message
			.ClusterInfo
			.Members
			.Where(IsAliveLeader);

		var leaderIsStillLeader = aliveLeaders.FirstOrDefault(x => x.InstanceId == _leader.InstanceId) is not null;

		if (!leaderIsStillLeader) {
			var noLeader = !aliveLeaders.Any();
			Log.Debug(
				(noLeader ? "NO LEADER found" : "LEADER CHANGE detected") + " in READ ONLY PRE-REPLICA/READ ONLY REPLICA state. Proceeding to READ ONLY LEADERLESS STATE. CURRENT LEADER: [{leader}]", _leader);
			_stateCorrelationId = Guid.NewGuid();
			_leaderConnectionCorrelationId = Guid.NewGuid();
			await _fsm.HandleAsync(new SystemMessage.BecomeReadOnlyLeaderless(_stateCorrelationId), token);
		}

		await _outputBus.DispatchAsync(message, token);
	}

	private async ValueTask HandleAsReadOnlyLeaderLess(GossipMessage.GossipUpdated message, CancellationToken token) {
		if (_leader is not null)
			return;

		var aliveLeaders = message.ClusterInfo.Members.Where(IsAliveLeader);
		var leaderCount = aliveLeaders.Count();
		if (leaderCount is 1) {
			_leader = aliveLeaders.First();
			Log.Information("LEADER found in READ ONLY LEADERLESS state. LEADER: [{leader}]. Proceeding to READ ONLY PRE-REPLICA state.", _leader);
			_stateCorrelationId = Guid.NewGuid();
			_leaderConnectionCorrelationId = Guid.NewGuid();
			await _fsm.HandleAsync(new SystemMessage.BecomePreReadOnlyReplica(_stateCorrelationId, _leaderConnectionCorrelationId, _leader), token);
		} else {
			Log.Debug(
				"{leadersFound} found in READ ONLY LEADERLESS state, making further attempts.",
				(leaderCount is 0 ? "NO LEADER" : "MULTIPLE LEADERS"));
		}

		await _outputBus.DispatchAsync(message, token);
	}

	private ValueTask HandleAsNonLeader(GossipMessage.GossipUpdated message, CancellationToken token) {
		if (_leader is null)
			return ValueTask.FromException(new Exception("_leader == null"));

		var leader = message.ClusterInfo.Members.FirstOrDefault(x => x.InstanceId == _leader.InstanceId);
		if (leader is null or { IsAlive: false }) {
			Log.Debug(
				"There is NO LEADER or LEADER is DEAD according to GOSSIP. Starting new elections. LEADER: [{leader}].",
				_leader);
			_mainQueue.Publish(new ElectionMessage.StartElections());
		} else if (leader.State is not VNodeState.PreLeader and not VNodeState.Leader and not VNodeState.ResigningLeader) {
			Log.Debug(
				"LEADER node is still alive but is no longer in a LEADER state according to GOSSIP. Starting new elections. LEADER: [{leader}].",
				_leader);
			_mainQueue.Publish(new ElectionMessage.StartElections());
		}

		return _outputBus.DispatchAsync(message, token);
	}

	private async ValueTask HandleAsDiscoverLeader(GossipMessage.GossipUpdated message, CancellationToken token) {
		if (_leader is not null)
			return;

		var aliveLeaders = message.ClusterInfo.Members.Where(IsAliveLeader);
		var leaderCount = aliveLeaders.Count();
		if (leaderCount is 1) {
			_leader = aliveLeaders.First();
			Log.Information("Existing LEADER found during LEADER DISCOVERY stage. LEADER: [{leader}]. Proceeding to PRE-REPLICA state.", _leader);
			_mainQueue.Publish(new LeaderDiscoveryMessage.LeaderFound(_leader));
			_stateCorrelationId = Guid.NewGuid();
			_leaderConnectionCorrelationId = Guid.NewGuid();
			await _fsm.HandleAsync(new SystemMessage.BecomePreReplica(_stateCorrelationId, _leaderConnectionCorrelationId, _leader), token);
		} else {
			Log.Debug(
				"{leadersFound} found during LEADER DISCOVERY stage, making further attempts.",
				(leaderCount is 0 ? "NO LEADER" : "MULTIPLE LEADERS"));
		}

		await _outputBus.DispatchAsync(message, token);
	}

	private ValueTask HandleAsDiscoverLeader(LeaderDiscoveryMessage.DiscoveryTimeout _, CancellationToken token) {
		if (_leader is not null)
			return ValueTask.CompletedTask;

		Log.Information("LEADER DISCOVERY timed out. Proceeding to UNKNOWN state.");
		return _fsm.HandleAsync(new SystemMessage.BecomeUnknown(Guid.NewGuid()), token);
	}

	private ValueTask Handle(SystemMessage.NoQuorumMessage message, CancellationToken token) {
		Log.Information("=== NO QUORUM EMERGED WITHIN TIMEOUT... RETIRING...");
		return _fsm.HandleAsync(new SystemMessage.BecomeUnknown(Guid.NewGuid()), token);
	}

	private ValueTask Handle(SystemMessage.WaitForChaserToCatchUp message, CancellationToken token) {
		return message.CorrelationId == _stateCorrelationId
			? _outputBus.DispatchAsync(message, token)
			: ValueTask.CompletedTask;
	}

	private ValueTask HandleAsPreLeader(SystemMessage.ChaserCaughtUp message, CancellationToken token) {
		if (_leader is null)
			return ValueTask.FromException(new Exception("_leader == null"));

		return _stateCorrelationId == message.CorrelationId
			? _outputBus.DispatchAsync(message, token)
			: ValueTask.CompletedTask;
	}

	private async ValueTask HandleAsPreReplica(SystemMessage.ChaserCaughtUp message, CancellationToken token) {
		if (_leader is null)
			throw new Exception("_leader == null");

		if (_stateCorrelationId != message.CorrelationId)
			return;

		await _outputBus.DispatchAsync(message, token);
		await _fsm.HandleAsync(
			new ReplicationMessage.SubscribeToLeader(_stateCorrelationId, _leader.InstanceId, Guid.NewGuid()), token);
	}

	private ValueTask Handle(ReplicationMessage.ReconnectToLeader message, CancellationToken token) {
		return _leader.InstanceId == message.Leader.InstanceId
			   && _leaderConnectionCorrelationId == message.ConnectionCorrelationId
			? _outputBus.DispatchAsync(message, token)
			: ValueTask.CompletedTask;
	}

	private ValueTask Handle(ReplicationMessage.LeaderConnectionFailed message, CancellationToken token) {
		if (_leader.InstanceId != message.Leader.InstanceId ||
			_leaderConnectionCorrelationId != message.LeaderConnectionCorrelationId)
			return ValueTask.CompletedTask;

		_leaderConnectionCorrelationId = Guid.NewGuid();
		var msg = new ReplicationMessage.ReconnectToLeader(_leaderConnectionCorrelationId, message.Leader);

		// Attempt the connection again after a timeout
		return _outputBus.DispatchAsync(TimerMessage.Schedule.Create(LeaderSubscriptionTimeout, _publishEnvelope, msg), token);
	}

	private async ValueTask Handle(ReplicationMessage.SubscribeToLeader message, CancellationToken token) {
		if (message.LeaderId != _leader.InstanceId || _stateCorrelationId != message.StateCorrelationId)
			return;

		_subscriptionId = message.SubscriptionId;
		await _outputBus.DispatchAsync(message, token);

		var msg = new ReplicationMessage.SubscribeToLeader(_stateCorrelationId, _leader.InstanceId, Guid.NewGuid());
		_mainQueue.Publish(TimerMessage.Schedule.Create(LeaderSubscriptionTimeout, _publishEnvelope, msg));
	}

	private async ValueTask Handle(ReplicationMessage.ReplicaSubscriptionRetry message, CancellationToken token) {
		if (IsLegitimateReplicationMessage(message)) {
			await _outputBus.DispatchAsync(message, token);

			var msg = new ReplicationMessage.SubscribeToLeader(_stateCorrelationId, _leader.InstanceId,
				Guid.NewGuid());
			_mainQueue.Publish(TimerMessage.Schedule.Create(LeaderSubscriptionRetryDelay, _publishEnvelope, msg));
		}
	}

	private async ValueTask Handle(ReplicationMessage.ReplicaSubscribed message, CancellationToken token) {
		if (IsLegitimateReplicationMessage(message)) {
			await _outputBus.DispatchAsync(message, token);

			Message msg = _nodeInfo.IsReadOnlyReplica
				? new SystemMessage.BecomeReadOnlyReplica(_stateCorrelationId, _leader)
				: new SystemMessage.BecomeCatchingUp(_stateCorrelationId, _leader);

			await _fsm.HandleAsync(msg, token);
		}
	}

	private ValueTask ForwardReplicationMessage<T>(T message, CancellationToken token) where T : Message, ReplicationMessage.IReplicationMessage {
		try {
			return IsLegitimateReplicationMessage(message)
				? _outputBus.DispatchAsync(message, token)
				: ValueTask.CompletedTask;
		} catch (Exception ex) {
			return ValueTask.FromException(ex);
		}
	}

	private async ValueTask Handle(ReplicationMessage.FollowerAssignment message, CancellationToken token) {
		if (IsLegitimateReplicationMessage(message)) {
			Log.Information(
				"========== [{httpEndPoint}] FOLLOWER ASSIGNMENT RECEIVED FROM [{internalTcp},{internalSecureTcp},{leaderId:B}].",
				_nodeInfo.HttpEndPoint,
				_leader.InternalTcpEndPoint == null ? "n/a" : _leader.InternalTcpEndPoint.ToString(),
				_leader.InternalSecureTcpEndPoint == null ? "n/a" : _leader.InternalSecureTcpEndPoint.ToString(),
				message.LeaderId);
			await _outputBus.DispatchAsync(message, token);
			await _fsm.HandleAsync(new SystemMessage.BecomeFollower(_stateCorrelationId, _leader), token);
		}
	}

	private async ValueTask Handle(ReplicationMessage.CloneAssignment message, CancellationToken token) {
		if (IsLegitimateReplicationMessage(message)) {
			Log.Information(
				"========== [{httpEndPoint}] CLONE ASSIGNMENT RECEIVED FROM [{internalTcp},{internalSecureTcp},{leaderId:B}].",
				_nodeInfo.HttpEndPoint,
				_leader.InternalTcpEndPoint == null ? "n/a" : _leader.InternalTcpEndPoint.ToString(),
				_leader.InternalSecureTcpEndPoint == null ? "n/a" : _leader.InternalSecureTcpEndPoint.ToString(),
				message.LeaderId);
			await _outputBus.DispatchAsync(message, token);
			await _fsm.HandleAsync(new SystemMessage.BecomeClone(_stateCorrelationId, _leader), token);
		}
	}

	private ValueTask Handle(ReplicationMessage.DropSubscription message, CancellationToken token) {
		ValueTask task;
		try {
			if (IsLegitimateReplicationMessage(message)) {
				Log.Information(
					"========== [{httpEndPoint}] DROP SUBSCRIPTION REQUEST RECEIVED FROM [{internalTcp},{internalSecureTcp},{leaderId:B}]. THIS MEANS THAT THERE IS A SURPLUS OF NODES IN THE CLUSTER, SHUTTING DOWN.",
					_nodeInfo.HttpEndPoint,
					_leader.InternalTcpEndPoint == null ? "n/a" : _leader.InternalTcpEndPoint.ToString(),
					_leader.InternalSecureTcpEndPoint == null ? "n/a" : _leader.InternalSecureTcpEndPoint.ToString(),
					message.LeaderId);
				task = _outputBus.DispatchAsync(new ClientMessage.RequestShutdown(exitProcess: true, shutdownHttp: true), token);
			} else {
				task = ValueTask.CompletedTask;
			}
		} catch (Exception ex) {
			task = ValueTask.FromException(ex);
		}

		return task;
	}

	private bool IsLegitimateReplicationMessage(ReplicationMessage.IReplicationMessage message) {
		if (message.SubscriptionId == Guid.Empty)
			throw new Exception("IReplicationMessage with empty SubscriptionId provided.");
		if (message.SubscriptionId != _subscriptionId) {
			Log.Debug(
				"Ignoring {message} because SubscriptionId {receivedSubscriptionId:B} is wrong. Current SubscriptionId is {subscriptionId:B}.",
				message.GetType().Name, message.SubscriptionId, _subscriptionId);
			return false;
		}

		if (_leader == null || _leader.InstanceId != message.LeaderId) {
			var msg = string.Format("{0} message passed SubscriptionId check, but leader is either null or wrong. "
									+ "Message.Leader: [{1:B}], VNode Leader: {2}.",
				message.GetType().Name, message.LeaderId, _leader);
			Log.Fatal("{messageType} message passed SubscriptionId check, but leader is either null or wrong. "
					  + "Message.Leader: [{leaderId:B}], VNode Leader: {leaderInfo}.",
				message.GetType().Name, message.LeaderId, _leader);
			Application.Exit(ExitCode.Error, msg);
			return false;
		}

		return true;
	}

	private async ValueTask Handle(SystemMessage.ServiceShutdown message, CancellationToken token) {
		var serviceInfo = message.ServiceName + (string.IsNullOrWhiteSpace(message.Details) ? "" : $" {message.Details}");
		Log.Information("========== [{httpEndPoint}] Service '{service}' has shut down.", _nodeInfo.HttpEndPoint,
			serviceInfo);

		_initializedServices[message.ServiceName] = ServiceState.Shutdown;

		if (_initializedServices.All(kvp => kvp.Value is ServiceState.Shutdown)) {
			Log.Information("========== [{httpEndPoint}] All Services Shutdown.", _nodeInfo.HttpEndPoint);
			await Shutdown(token);
		}

		await _outputBus.DispatchAsync(message, token);
	}

	private async ValueTask Handle(SystemMessage.ShutdownTimeout message, CancellationToken token) {
		Debug.Assert(State is VNodeState.ShuttingDown);

		var services = string.Join(", ", _initializedServices
			.Where(kvp => kvp.Value is not ServiceState.Shutdown)
			.Select(kvp => kvp.Key));
		var error = $"Gave up waiting for services: [{services}]";

		Log.Error("========== [{httpEndPoint}] Shutdown Timeout. {error}", _nodeInfo.HttpEndPoint, error);

		await Shutdown(token);
		await _outputBus.DispatchAsync(message, token);
	}

	private async ValueTask Shutdown(CancellationToken token) {
		Debug.Assert(State is VNodeState.ShuttingDown);

		await _db.Close(token);
		await _fsm.HandleAsync(new SystemMessage.BecomeShutdown(_stateCorrelationId), token);
	}

	private static bool IsAliveLeader(MemberInfo member)
		=> member is { IsAlive: true, State: VNodeState.Leader };
}
