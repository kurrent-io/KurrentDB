using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.TransactionLog.Chunks;
using ILogger = Serilog.ILogger;
using OperationResult = EventStore.Core.Messages.OperationResult;

namespace EventStore.Core.Services.VNode {
	public abstract class ClusterVNodeController {
		protected static readonly ILogger Log = Serilog.Log.ForContext<ClusterVNodeController>();
	}

	public class ClusterVNodeController<TStreamId> : ClusterVNodeController, IHandle<Message> {
		public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
		public static readonly TimeSpan LeaderReconnectionDelay = TimeSpan.FromMilliseconds(500);
		private static readonly TimeSpan LeaderSubscriptionRetryDelay = TimeSpan.FromMilliseconds(500);
		private static readonly TimeSpan LeaderSubscriptionTimeout = TimeSpan.FromMilliseconds(1000);
		private static readonly TimeSpan LeaderDiscoveryTimeout = TimeSpan.FromMilliseconds(3000);

		private readonly IPublisher _outputBus;
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

		private IQueuedHandler _mainQueue;
		private IEnvelope _publishEnvelope;
		private readonly VNodeFSM _fsm;

		private readonly MessageForwardingProxy _forwardingProxy;
		private readonly TimeSpan _forwardingTimeout;
		private readonly int _subsystemCount;
		private readonly Action _startSubsystems;

		private int _subSystemInitsToExpect;

		private int _serviceInitsToExpect = 1 /* StorageChaser */
											+ 1 /* StorageReader */
											+ 1 /* StorageWriter */;

		private int _serviceShutdownsToExpect = 1 /* StorageChaser */
												+ 1 /* StorageReader */
												+ 1 /* StorageWriter */
												+ 1 /* IndexCommitterService */
												+ 1 /* LeaderReplicationService */
												+ 1 /* HttpService */;

		private bool _exitProcessOnShutdown;

		public ClusterVNodeController(IPublisher outputBus, VNodeInfo nodeInfo, TFChunkDb db,
			INodeStatusTracker statusTracker,
			ClusterVNodeOptions options, ClusterVNode<TStreamId> node, MessageForwardingProxy forwardingProxy,
			Action startSubsystems) {
			Ensure.NotNull(outputBus, "outputBus");
			Ensure.NotNull(nodeInfo, "nodeInfo");
			Ensure.NotNull(db, "dbConfig");
			Ensure.NotNull(node, "node");
			Ensure.NotNull(forwardingProxy, "forwardingProxy");
			Ensure.NotNull(startSubsystems, "startSubsystems");

			_outputBus = outputBus;
			_nodeInfo = nodeInfo;
			_db = db;
			_node = node;
			_statusTracker = statusTracker;
			_startSubsystems = startSubsystems;
			_subsystemCount = options.Subsystems.Count;
			_subSystemInitsToExpect = _subsystemCount;
			_clusterSize = options.Cluster.ClusterSize;
			if (_clusterSize == 1) {
				_serviceShutdownsToExpect = 1 /* StorageChaser */
											+ 1 /* StorageReader */
											+ 1 /* StorageWriter */
											+ 1 /* IndexCommitterService */
											+ 1 /* HttpService */;
			}


			_forwardingProxy = forwardingProxy;
			_forwardingTimeout = TimeSpan.FromMilliseconds(options.Database.PrepareTimeoutMs +
														   options.Database.CommitTimeoutMs + 300);

			_fsm = CreateFSM();
		}

		public void SetMainQueue(IQueuedHandler mainQueue) {
			Ensure.NotNull(mainQueue, "mainQueue");

			_mainQueue = mainQueue;
			_publishEnvelope = new PublishEnvelope(mainQueue);
		}

		private VNodeFSM CreateFSM() {
			var stm = new VNodeFSMBuilder(() => State)
				.InAnyState()
				.When<SystemMessage.StateChangeMessage>()
					.Do(m => Application.Exit(ExitCode.Error,
						string.Format("{0} message was unhandled in {1}. State: {2}", m.GetType().Name, GetType().Name, State)))
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
				.InAllStatesExcept(VNodeState.ShuttingDown, VNodeState.Shutdown)
				.When<ClientMessage.RequestShutdown>().Do(Handle)
				.When<SystemMessage.BecomeShuttingDown>().Do(Handle)
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

		void IHandle<Message>.Handle(Message message) {
			_fsm.Handle(message);
		}

		private void Handle(SystemMessage.SystemInit message) {
			Log.Information("========== [{httpEndPoint}] SYSTEM INIT...", _nodeInfo.HttpEndPoint);
			_outputBus.Publish(message);
		}

		private void Handle(SystemMessage.SystemStart message) {
			Log.Information("========== [{httpEndPoint}] SYSTEM START...", _nodeInfo.HttpEndPoint);
			_outputBus.Publish(message);
			if (_nodeInfo.IsReadOnlyReplica) {
				_fsm.Handle(new SystemMessage.BecomeReadOnlyLeaderless(Guid.NewGuid()));
			} else {
				if (_clusterSize > 1) {
					_fsm.Handle(new SystemMessage.BecomeDiscoverLeader(Guid.NewGuid()));
				} else {
					_fsm.Handle(new SystemMessage.BecomeUnknown(Guid.NewGuid()));
				}
			}
		}

		private void Handle(SystemMessage.BecomeUnknown message) {
			Log.Information("========== [{httpEndPoint}] IS UNKNOWN...", _nodeInfo.HttpEndPoint);

			State = VNodeState.Unknown;
			_leader = null;
			_outputBus.Publish(message);
			_mainQueue.Publish(new ElectionMessage.StartElections());
		}

		private void Handle(SystemMessage.BecomeDiscoverLeader message) {
			Log.Information("========== [{httpEndPoint}] IS ATTEMPTING TO DISCOVER EXISTING LEADER...", _nodeInfo.HttpEndPoint);

			State = VNodeState.DiscoverLeader;
			_outputBus.Publish(message);

			var msg = new LeaderDiscoveryMessage.DiscoveryTimeout();
			_mainQueue.Publish(TimerMessage.Schedule.Create(LeaderDiscoveryTimeout, _publishEnvelope, msg));
		}

		private void Handle(SystemMessage.InitiateLeaderResignation message) {
			Log.Information("========== [{httpEndPoint}] IS INITIATING LEADER RESIGNATION...", _nodeInfo.HttpEndPoint);

			_fsm.Handle(new SystemMessage.BecomeResigningLeader(_stateCorrelationId));
		}

		private void Handle(SystemMessage.BecomeResigningLeader message) {
			Log.Information("========== [{httpEndPoint}] IS RESIGNING LEADER...", _nodeInfo.HttpEndPoint);
			if (_stateCorrelationId != message.CorrelationId)
				return;

			State = VNodeState.ResigningLeader;
			_outputBus.Publish(message);
		}

		private void Handle(SystemMessage.RequestQueueDrained message) {
			Log.Information("========== [{httpEndPoint}] REQUEST QUEUE DRAINED. RESIGNATION COMPLETE.", _nodeInfo.HttpEndPoint);
			_fsm.Handle(new SystemMessage.BecomeUnknown(Guid.NewGuid()));
		}

		private void Handle(SystemMessage.BecomePreReplica message) {
			if (_leader == null)
				throw new Exception("_leader == null");
			if (_stateCorrelationId != message.CorrelationId)
				return;

			Log.Information(
				"========== [{httpEndPoint}] PRE-REPLICA STATE, WAITING FOR CHASER TO CATCH UP... LEADER IS [{masterHttp},{masterId:B}]",
				_nodeInfo.HttpEndPoint, _leader.HttpEndPoint, _leader.InstanceId);
			State = VNodeState.PreReplica;
			_outputBus.Publish(message);
			_mainQueue.Publish(new SystemMessage.WaitForChaserToCatchUp(_stateCorrelationId, TimeSpan.Zero));
		}

		private void Handle(SystemMessage.BecomePreReadOnlyReplica message) {
			if (_stateCorrelationId != message.CorrelationId)
				return;
			if (_leader == null)
				throw new Exception("_leader == null");

			Log.Information(
				"========== [{httpEndPoint}] READ ONLY PRE-REPLICA STATE, WAITING FOR CHASER TO CATCH UP... LEADER IS [{leaderHttp},{leaderId:B}]",
				_nodeInfo.HttpEndPoint, _leader.HttpEndPoint, _leader.InstanceId);
			State = VNodeState.PreReadOnlyReplica;
			_outputBus.Publish(message);
			_mainQueue.Publish(new SystemMessage.WaitForChaserToCatchUp(_stateCorrelationId, TimeSpan.Zero));
		}

		private void Handle(SystemMessage.BecomeCatchingUp message) {
			if (_leader == null)
				throw new Exception("_leader == null");
			if (_stateCorrelationId != message.CorrelationId)
				return;

			Log.Information("========== [{httpEndPoint}] IS CATCHING UP... LEADER IS [{leaderHttp},{leaderId:B}]",
				_nodeInfo.HttpEndPoint, _leader.HttpEndPoint, _leader.InstanceId);
			State = VNodeState.CatchingUp;
			_outputBus.Publish(message);
		}

		private void Handle(SystemMessage.BecomeClone message) {
			if (_leader == null)
				throw new Exception("_leader == null");
			if (_stateCorrelationId != message.CorrelationId)
				return;

			Log.Information("========== [{httpEndPoint}] IS CLONE... LEADER IS [{leaderHttp},{leaderId:B}]",
				_nodeInfo.HttpEndPoint, _leader.HttpEndPoint, _leader.InstanceId);
			State = VNodeState.Clone;
			_outputBus.Publish(message);
		}

		private void Handle(SystemMessage.BecomeFollower message) {
			if (_leader == null)
				throw new Exception("_leader == null");
			if (_stateCorrelationId != message.CorrelationId)
				return;

			Log.Information("========== [{httpEndPoint}] IS FOLLOWER... LEADER IS [{leaderHttp},{leaderId:B}]",
				_nodeInfo.HttpEndPoint, _leader.HttpEndPoint, _leader.InstanceId);
			State = VNodeState.Follower;
			_outputBus.Publish(message);
		}

		private void Handle(SystemMessage.BecomeReadOnlyLeaderless message) {
			Log.Information("========== [{httpEndPoint}] IS READ ONLY REPLICA WITH UNKNOWN LEADER...", _nodeInfo.HttpEndPoint);
			State = VNodeState.ReadOnlyLeaderless;
			_leader = null;
			_outputBus.Publish(message);
		}

		private void Handle(SystemMessage.BecomeReadOnlyReplica message) {
			if (_leader == null)
				throw new Exception("_leader == null");
			if (_stateCorrelationId != message.CorrelationId)
				return;

			Log.Information("========== [{httpEndPoint}] IS READ ONLY REPLICA... LEADER IS [{leaderHttp},{leaderId:B}]",
				_nodeInfo.HttpEndPoint, _leader.HttpEndPoint, _leader.InstanceId);
			State = VNodeState.ReadOnlyReplica;
			_outputBus.Publish(message);
		}

		private void Handle(SystemMessage.BecomePreLeader message) {
			if (_leader == null)
				throw new Exception("_leader == null");
			if (_stateCorrelationId != message.CorrelationId)
				return;

			Log.Information("========== [{httpEndPoint}] PRE-LEADER STATE, WAITING FOR CHASER TO CATCH UP...",
				_nodeInfo.HttpEndPoint);
			State = VNodeState.PreLeader;
			_outputBus.Publish(message);
			_mainQueue.Publish(new SystemMessage.WaitForChaserToCatchUp(_stateCorrelationId, TimeSpan.Zero));
		}

		private void Handle(SystemMessage.BecomeLeader message) {
			if (State == VNodeState.Leader)
				throw new Exception("We should not BecomeLeader twice in a row.");
			if (_leader == null)
				throw new Exception("_leader == null");
			if (_stateCorrelationId != message.CorrelationId)
				return;

			Log.Information("========== [{httpEndPoint}] IS LEADER... SPARTA!", _nodeInfo.HttpEndPoint);
			State = VNodeState.Leader;
			_outputBus.Publish(message);
		}

		private void Handle(SystemMessage.BecomeShuttingDown message) {
			if (State == VNodeState.ShuttingDown || State == VNodeState.Shutdown)
				return;

			Log.Information("========== [{httpEndPoint}] IS SHUTTING DOWN...", _nodeInfo.HttpEndPoint);
			_leader = null;
			_stateCorrelationId = message.CorrelationId;
			_exitProcessOnShutdown = message.ExitProcess;
			State = VNodeState.ShuttingDown;
			_mainQueue.Publish(TimerMessage.Schedule.Create(ShutdownTimeout, _publishEnvelope,
				new SystemMessage.ShutdownTimeout()));
			_outputBus.Publish(message);
		}

		private void Handle(SystemMessage.BecomeShutdown message) {
			Log.Information("========== [{httpEndPoint}] IS SHUT DOWN.", _nodeInfo.HttpEndPoint);
			State = VNodeState.Shutdown;
			try {
				_outputBus.Publish(message);
			} catch (Exception exc) {
				Log.Error(exc, "Error when publishing {message}.", message);
			}

			try {
				_node.WorkersHandler.Stop();
				_mainQueue.RequestStop();
			} catch (Exception exc) {
				Log.Error(exc, "Error when stopping workers/main queue.");
			}

			if (_exitProcessOnShutdown) {
				Application.Exit(ExitCode.Success, "Shutdown and exit from process was requested.");
			}
		}

		private void Handle(ElectionMessage.ElectionsDone message) {
			if (_leader != null && _leader.InstanceId == message.Leader.InstanceId) {
				//if the leader hasn't changed, we skip state changes through PreLeader or PreReplica
				if (_leader.InstanceId == _nodeInfo.InstanceId && State == VNodeState.Leader) {
					//transitioning from leader to leader, we just write a new epoch
					_fsm.Handle(new SystemMessage.WriteEpoch(message.ProposalNumber));
				}

				return;
			}

			_leader = message.Leader;
			_subscriptionId = Guid.NewGuid();
			_stateCorrelationId = Guid.NewGuid();
			_leaderConnectionCorrelationId = Guid.NewGuid();
			_outputBus.Publish(message);
			if (_leader.InstanceId == _nodeInfo.InstanceId)
				_fsm.Handle(new SystemMessage.BecomePreLeader(_stateCorrelationId));
			else
				_fsm.Handle(new SystemMessage.BecomePreReplica(_stateCorrelationId, _leaderConnectionCorrelationId, _leader));
		}

		private void Handle(SystemMessage.ServiceInitialized message) {
			Log.Information("========== [{httpEndPoint}] Service '{service}' initialized.", _nodeInfo.HttpEndPoint,
				message.ServiceName);
			_serviceInitsToExpect -= 1;
			_outputBus.Publish(message);
			if (_serviceInitsToExpect == 0)
				_mainQueue.Publish(new SystemMessage.SystemStart());
		}

		private void Handle(AuthenticationMessage.AuthenticationProviderInitialized message) {
			_startSubsystems();
			_outputBus.Publish(message);
			_fsm.Handle(new SystemMessage.SystemCoreReady());
		}

		private void Handle(AuthenticationMessage.AuthenticationProviderInitializationFailed message) {
			Log.Error("Authentication Provider Initialization Failed. Shutting Down.");
			_fsm.Handle(new SystemMessage.BecomeShutdown(Guid.NewGuid()));
		}

		private void Handle(SystemMessage.SystemCoreReady message) {
			if (_subsystemCount == 0) {
				_outputBus.Publish(new SystemMessage.SystemReady());
			} else {
				_outputBus.Publish(message);
			}
		}

		private void Handle(SystemMessage.SubSystemInitialized message) {
			Log.Information("========== [{httpEndPoint}] Sub System '{subSystemName}' initialized.", _nodeInfo.HttpEndPoint,
				message.SubSystemName);
			if (Interlocked.Decrement(ref _subSystemInitsToExpect) == 0) {
				_outputBus.Publish(new SystemMessage.SystemReady());
			}
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

		private void HandleAsNonLeader(ClientMessage.ReadEvent message) {
			if (message.RequireLeader) {
				if (_leader == null)
					DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
				else
					DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
			} else {
				_outputBus.Publish(message);
			}
		}

		private void HandleAsNonLeader(ClientMessage.ReadStreamEventsForward message) {
			if (message.RequireLeader) {
				if (_leader == null)
					DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
				else
					DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
			} else {
				_outputBus.Publish(message);
			}
		}

		private void HandleAsNonLeader(ClientMessage.ReadStreamEventsBackward message) {
			if (message.RequireLeader) {
				if (_leader == null)
					DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
				else
					DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
			} else {
				_outputBus.Publish(message);
			}
		}

		private void HandleAsNonLeader(ClientMessage.ReadAllEventsForward message) {
			if (message.RequireLeader) {
				if (_leader == null)
					DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
				else
					DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
			} else {
				_outputBus.Publish(message);
			}
		}

		private void HandleAsNonLeader(ClientMessage.FilteredReadAllEventsForward message) {
			if (message.RequireLeader) {
				if (_leader == null)
					DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
				else
					DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
			} else {
				_outputBus.Publish(message);
			}
		}

		private void HandleAsNonLeader(ClientMessage.ReadAllEventsBackward message) {
			if (message.RequireLeader) {
				if (_leader == null)
					DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
				else
					DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
			} else {
				_outputBus.Publish(message);
			}
		}

		private void HandleAsNonLeader(ClientMessage.FilteredReadAllEventsBackward message) {
			if (message.RequireLeader) {
				if (_leader == null)
					DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
				else
					DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
			} else {
				_outputBus.Publish(message);
			}
		}

		private void HandleAsNonLeader(ClientMessage.CreatePersistentSubscriptionToStream message) {
			if (_leader == null)
				DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
			else
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
		}

		private void HandleAsNonLeader(ClientMessage.ConnectToPersistentSubscriptionToStream message) {
			if (_leader == null)
				DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
			else
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
		}

		private void HandleAsNonLeader(ClientMessage.UpdatePersistentSubscriptionToStream message) {
			if (_leader == null)
				DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
			else
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
		}

		private void HandleAsNonLeader(ClientMessage.DeletePersistentSubscriptionToStream message) {
			if (_leader == null)
				DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
			else
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
		}

		private void HandleAsNonLeader(ClientMessage.CreatePersistentSubscriptionToAll message) {
			if (_leader == null)
				DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
			else
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
		}

		private void HandleAsNonLeader(ClientMessage.ConnectToPersistentSubscriptionToAll message) {
			if (_leader == null)
				DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
			else
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
		}

		private void HandleAsNonLeader(ClientMessage.UpdatePersistentSubscriptionToAll message) {
			if (_leader == null)
				DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
			else
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
		}

		private void HandleAsNonLeader(ClientMessage.DeletePersistentSubscriptionToAll message) {
			if (_leader == null)
				DenyRequestBecauseNotReady(message.Envelope, message.CorrelationId);
			else
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
		}

		private void HandleAsNonLeader(ClientMessage.WriteEvents message) {
			if (message.RequireLeader) {
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
				return;
			}

			var timeoutMessage = new ClientMessage.WriteEventsCompleted(
				message.CorrelationId, OperationResult.ForwardTimeout, "Forwarding timeout");
			ForwardRequest(message, timeoutMessage);
		}

		private void HandleAsNonLeader(ClientMessage.TransactionStart message) {
			if (message.RequireLeader) {
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
				return;
			}

			var timeoutMessage = new ClientMessage.TransactionStartCompleted(
				message.CorrelationId, -1, OperationResult.ForwardTimeout, "Forwarding timeout");
			ForwardRequest(message, timeoutMessage);
		}

		private void HandleAsNonLeader(ClientMessage.TransactionWrite message) {
			if (message.RequireLeader) {
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
				return;
			}

			var timeoutMessage = new ClientMessage.TransactionWriteCompleted(
				message.CorrelationId, message.TransactionId, OperationResult.ForwardTimeout, "Forwarding timeout");
			ForwardRequest(message, timeoutMessage);
		}

		private void HandleAsNonLeader(ClientMessage.TransactionCommit message) {
			if (message.RequireLeader) {
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
				return;
			}

			var timeoutMessage = new ClientMessage.TransactionCommitCompleted(
				message.CorrelationId, message.TransactionId, OperationResult.ForwardTimeout, "Forwarding timeout");
			ForwardRequest(message, timeoutMessage);
		}

		private void HandleAsNonLeader(ClientMessage.DeleteStream message) {
			if (message.RequireLeader) {
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
				return;
			}

			var timeoutMessage = new ClientMessage.DeleteStreamCompleted(
				message.CorrelationId, OperationResult.ForwardTimeout, "Forwarding timeout", -1, -1, -1);
			ForwardRequest(message, timeoutMessage);
		}

		private void ForwardRequest(ClientMessage.WriteRequestMessage msg, Message timeoutMessage) {
			_forwardingProxy.Register(msg.InternalCorrId, msg.CorrelationId, msg.Envelope, _forwardingTimeout,
				timeoutMessage);
			_outputBus.Publish(new ClientMessage.TcpForwardMessage(msg));
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

		private void HandleAsReadOnlyReplica(ClientMessage.WriteEvents message) {
			if (message.RequireLeader) {
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
				return;
			}
			if (message.User != SystemAccounts.System) {
				DenyRequestBecauseReadOnly(message.CorrelationId, message.Envelope);
				return;
			}

			var timeoutMessage = new ClientMessage.WriteEventsCompleted(
				message.CorrelationId, OperationResult.ForwardTimeout, "Forwarding timeout");
			ForwardRequest(message, timeoutMessage);
		}

		private void HandleAsReadOnlyReplica(ClientMessage.TransactionStart message) {
			if (message.RequireLeader) {
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
				return;
			}
			if (message.User != SystemAccounts.System) {
				DenyRequestBecauseReadOnly(message.CorrelationId, message.Envelope);
				return;
			}
			var timeoutMessage = new ClientMessage.TransactionStartCompleted(
				message.CorrelationId, -1, OperationResult.ForwardTimeout, "Forwarding timeout");
			ForwardRequest(message, timeoutMessage);
		}

		private void HandleAsReadOnlyReplica(ClientMessage.TransactionWrite message) {
			if (message.RequireLeader) {
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
				return;
			}
			if (message.User != SystemAccounts.System) {
				DenyRequestBecauseReadOnly(message.CorrelationId, message.Envelope);
				return;
			}

			var timeoutMessage = new ClientMessage.TransactionWriteCompleted(
				message.CorrelationId, message.TransactionId, OperationResult.ForwardTimeout, "Forwarding timeout");
			ForwardRequest(message, timeoutMessage);
		}

		private void HandleAsReadOnlyReplica(ClientMessage.TransactionCommit message) {
			if (message.RequireLeader) {
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
				return;
			}
			if (message.User != SystemAccounts.System) {
				DenyRequestBecauseReadOnly(message.CorrelationId, message.Envelope);
				return;
			}

			var timeoutMessage = new ClientMessage.TransactionCommitCompleted(
				message.CorrelationId, message.TransactionId, OperationResult.ForwardTimeout, "Forwarding timeout");
			ForwardRequest(message, timeoutMessage);
		}

		private void HandleAsReadOnlyReplica(ClientMessage.DeleteStream message) {
			if (message.RequireLeader) {
				DenyRequestBecauseNotLeader(message.CorrelationId, message.Envelope);
				return;
			}
			if (message.User != SystemAccounts.System) {
				DenyRequestBecauseReadOnly(message.CorrelationId, message.Envelope);
				return;
			}
			var timeoutMessage = new ClientMessage.DeleteStreamCompleted(
				message.CorrelationId, OperationResult.ForwardTimeout, "Forwarding timeout", -1, -1, -1);
			ForwardRequest(message, timeoutMessage);
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

		private void Handle(SystemMessage.VNodeConnectionLost message) {
			if (_leader != null && _leader.Is(message.VNodeEndPoint)) // leader connection failed
			{
				_leaderConnectionCorrelationId = Guid.NewGuid();
				var msg = State == VNodeState.PreReplica
					? (Message)new ReplicationMessage.ReconnectToLeader(_leaderConnectionCorrelationId, _leader)
					: new SystemMessage.BecomePreReplica(_stateCorrelationId, _leaderConnectionCorrelationId, _leader);
				_mainQueue.Publish(TimerMessage.Schedule.Create(LeaderReconnectionDelay, _publishEnvelope, msg));
			}

			_outputBus.Publish(message);
		}

		private void HandleAsReadOnlyReplica(SystemMessage.VNodeConnectionLost message) {
			if (_leader != null && _leader.Is(message.VNodeEndPoint)) // leader connection failed
			{
				_leaderConnectionCorrelationId = Guid.NewGuid();
				var msg = State == VNodeState.PreReadOnlyReplica
					? (Message)new ReplicationMessage.ReconnectToLeader(_leaderConnectionCorrelationId, _leader)
					: new SystemMessage.BecomePreReadOnlyReplica(_stateCorrelationId, _leaderConnectionCorrelationId, _leader);
				_mainQueue.Publish(TimerMessage.Schedule.Create(LeaderReconnectionDelay, _publishEnvelope, msg));
			}

			_outputBus.Publish(message);
		}

		private void HandleAsLeader(GossipMessage.GossipUpdated message) {
			if (_leader == null)
				throw new Exception("_leader == null");
			if (message.ClusterInfo.Members.Count(x => x.IsAlive && x.State == VNodeState.Leader) > 1) {
				Log.Debug("There are MULTIPLE LEADERS according to gossip, need to start elections. LEADER: [{leader}]",
					_leader);
				Log.Debug("GOSSIP:");
				Log.Debug("{clusterInfo}", message.ClusterInfo);
				_mainQueue.Publish(new ElectionMessage.StartElections());
			}

			_outputBus.Publish(message);
		}

		private void HandleAsReadOnlyReplica(GossipMessage.GossipUpdated message) {
			if (_leader == null)
				throw new Exception("_leader == null");

			var aliveLeaders = message.ClusterInfo.Members.Where(x => x.IsAlive && x.State == VNodeState.Leader);
			var leaderIsStillLeader = aliveLeaders.FirstOrDefault(x => x.InstanceId == _leader.InstanceId) != null;

			if (!leaderIsStillLeader) {
				var noLeader = !aliveLeaders.Any();
				Log.Debug(
					(noLeader ? "NO LEADER found" : "LEADER CHANGE detected") + " in READ ONLY PRE-REPLICA/READ ONLY REPLICA state. Proceeding to READ ONLY LEADERLESS STATE. CURRENT LEADER: [{leader}]", _leader);
				_stateCorrelationId = Guid.NewGuid();
				_leaderConnectionCorrelationId = Guid.NewGuid();
				_fsm.Handle(new SystemMessage.BecomeReadOnlyLeaderless(_stateCorrelationId));
			}

			_outputBus.Publish(message);
		}

		private void HandleAsReadOnlyLeaderLess(GossipMessage.GossipUpdated message) {
			if (_leader != null)
				return;

			var aliveLeaders = message.ClusterInfo.Members.Where(x => x.IsAlive && x.State == VNodeState.Leader);
			var leaderCount = aliveLeaders.Count();
			if (leaderCount == 1) {
				_leader = aliveLeaders.First();
				Log.Information("LEADER found in READ ONLY LEADERLESS state. LEADER: [{leader}]. Proceeding to READ ONLY PRE-REPLICA state.", _leader);
				_stateCorrelationId = Guid.NewGuid();
				_leaderConnectionCorrelationId = Guid.NewGuid();
				_fsm.Handle(new SystemMessage.BecomePreReadOnlyReplica(_stateCorrelationId, _leaderConnectionCorrelationId, _leader));
			} else {
				Log.Debug(
					"{leadersFound} found in READ ONLY LEADERLESS state, making further attempts.",
					(leaderCount == 0 ? "NO LEADER" : "MULTIPLE LEADERS"));
			}

			_outputBus.Publish(message);
		}

		private void HandleAsNonLeader(GossipMessage.GossipUpdated message) {
			if (_leader == null)
				throw new Exception("_leader == null");
			var leader = message.ClusterInfo.Members.FirstOrDefault(x => x.InstanceId == _leader.InstanceId);
			if (leader == null || !leader.IsAlive) {
				Log.Debug(
					"There is NO LEADER or LEADER is DEAD according to GOSSIP. Starting new elections. LEADER: [{leader}].",
					_leader);
				_mainQueue.Publish(new ElectionMessage.StartElections());
			} else if (leader.State != VNodeState.PreLeader && leader.State != VNodeState.Leader && leader.State != VNodeState.ResigningLeader) {
				Log.Debug(
					"LEADER node is still alive but is no longer in a LEADER state according to GOSSIP. Starting new elections. LEADER: [{leader}].",
					_leader);
				_mainQueue.Publish(new ElectionMessage.StartElections());
			}
			_outputBus.Publish(message);
		}

		private void HandleAsDiscoverLeader(GossipMessage.GossipUpdated message) {
			if (_leader != null)
				return;

			var aliveLeaders = message.ClusterInfo.Members.Where(x => x.IsAlive && x.State == VNodeState.Leader);
			var leaderCount = aliveLeaders.Count();
			if (leaderCount == 1) {
				_leader = aliveLeaders.First();
				Log.Information("Existing LEADER found during LEADER DISCOVERY stage. LEADER: [{leader}]. Proceeding to PRE-REPLICA state.", _leader);
				_mainQueue.Publish(new LeaderDiscoveryMessage.LeaderFound(_leader));
				_stateCorrelationId = Guid.NewGuid();
				_leaderConnectionCorrelationId = Guid.NewGuid();
				_fsm.Handle(new SystemMessage.BecomePreReplica(_stateCorrelationId, _leaderConnectionCorrelationId, _leader));
			} else {
				Log.Debug(
					"{leadersFound} found during LEADER DISCOVERY stage, making further attempts.",
					(leaderCount == 0 ? "NO LEADER" : "MULTIPLE LEADERS"));
			}

			_outputBus.Publish(message);
		}

		private void HandleAsDiscoverLeader(LeaderDiscoveryMessage.DiscoveryTimeout _) {
			if (_leader != null)
				return;
			Log.Information("LEADER DISCOVERY timed out. Proceeding to UNKNOWN state.");
			_fsm.Handle(new SystemMessage.BecomeUnknown(Guid.NewGuid()));
		}

		private void Handle(SystemMessage.NoQuorumMessage message) {
			Log.Information("=== NO QUORUM EMERGED WITHIN TIMEOUT... RETIRING...");
			_fsm.Handle(new SystemMessage.BecomeUnknown(Guid.NewGuid()));
		}

		private void Handle(SystemMessage.WaitForChaserToCatchUp message) {
			if (message.CorrelationId != _stateCorrelationId)
				return;
			_outputBus.Publish(message);
		}

		private void HandleAsPreLeader(SystemMessage.ChaserCaughtUp message) {
			if (_leader == null)
				throw new Exception("_leader == null");
			if (_stateCorrelationId != message.CorrelationId)
				return;

			_outputBus.Publish(message);
		}

		private void HandleAsPreReplica(SystemMessage.ChaserCaughtUp message) {
			if (_leader == null)
				throw new Exception("_leader == null");
			if (_stateCorrelationId != message.CorrelationId)
				return;
			_outputBus.Publish(message);
			_fsm.Handle(
				new ReplicationMessage.SubscribeToLeader(_stateCorrelationId, _leader.InstanceId, Guid.NewGuid()));
		}

		private void Handle(ReplicationMessage.ReconnectToLeader message) {
			if (_leader.InstanceId != message.Leader.InstanceId || _leaderConnectionCorrelationId != message.ConnectionCorrelationId)
				return;
			_outputBus.Publish(message);
		}

		private void Handle(ReplicationMessage.LeaderConnectionFailed message) {
			if (_leader.InstanceId != message.Leader.InstanceId || _leaderConnectionCorrelationId != message.LeaderConnectionCorrelationId)
				return;
			_leaderConnectionCorrelationId = Guid.NewGuid();
			var msg = new ReplicationMessage.ReconnectToLeader(_leaderConnectionCorrelationId, message.Leader);
			// Attempt the connection again after a timeout
			_outputBus.Publish(TimerMessage.Schedule.Create(LeaderSubscriptionTimeout, _publishEnvelope, msg));
		}

		private void Handle(ReplicationMessage.SubscribeToLeader message) {
			if (message.LeaderId != _leader.InstanceId || _stateCorrelationId != message.StateCorrelationId)
				return;
			_subscriptionId = message.SubscriptionId;
			_outputBus.Publish(message);

			var msg = new ReplicationMessage.SubscribeToLeader(_stateCorrelationId, _leader.InstanceId, Guid.NewGuid());
			_mainQueue.Publish(TimerMessage.Schedule.Create(LeaderSubscriptionTimeout, _publishEnvelope, msg));
		}

		private void Handle(ReplicationMessage.ReplicaSubscriptionRetry message) {
			if (IsLegitimateReplicationMessage(message)) {
				_outputBus.Publish(message);

				var msg = new ReplicationMessage.SubscribeToLeader(_stateCorrelationId, _leader.InstanceId,
					Guid.NewGuid());
				_mainQueue.Publish(TimerMessage.Schedule.Create(LeaderSubscriptionRetryDelay, _publishEnvelope, msg));
			}
		}

		private void Handle(ReplicationMessage.ReplicaSubscribed message) {
			if (IsLegitimateReplicationMessage(message)) {
				_outputBus.Publish(message);
				if (_nodeInfo.IsReadOnlyReplica) {
					_fsm.Handle(new SystemMessage.BecomeReadOnlyReplica(_stateCorrelationId, _leader));
				} else {
					_fsm.Handle(new SystemMessage.BecomeCatchingUp(_stateCorrelationId, _leader));
				}
			}
		}

		private void ForwardReplicationMessage<T>(T message) where T : Message, ReplicationMessage.IReplicationMessage {
			if (IsLegitimateReplicationMessage(message))
				_outputBus.Publish(message);
		}

		private void Handle(ReplicationMessage.FollowerAssignment message) {
			if (IsLegitimateReplicationMessage(message)) {
				Log.Information(
					"========== [{httpEndPoint}] FOLLOWER ASSIGNMENT RECEIVED FROM [{internalTcp},{internalSecureTcp},{leaderId:B}].",
					_nodeInfo.HttpEndPoint,
					_leader.InternalTcpEndPoint == null ? "n/a" : _leader.InternalTcpEndPoint.ToString(),
					_leader.InternalSecureTcpEndPoint == null ? "n/a" : _leader.InternalSecureTcpEndPoint.ToString(),
					message.LeaderId);
				_outputBus.Publish(message);
				_fsm.Handle(new SystemMessage.BecomeFollower(_stateCorrelationId, _leader));
			}
		}

		private void Handle(ReplicationMessage.CloneAssignment message) {
			if (IsLegitimateReplicationMessage(message)) {
				Log.Information(
					"========== [{httpEndPoint}] CLONE ASSIGNMENT RECEIVED FROM [{internalTcp},{internalSecureTcp},{leaderId:B}].",
					_nodeInfo.HttpEndPoint,
					_leader.InternalTcpEndPoint == null ? "n/a" : _leader.InternalTcpEndPoint.ToString(),
					_leader.InternalSecureTcpEndPoint == null ? "n/a" : _leader.InternalSecureTcpEndPoint.ToString(),
					message.LeaderId);
				_outputBus.Publish(message);
				_fsm.Handle(new SystemMessage.BecomeClone(_stateCorrelationId, _leader));
			}
		}

		private void Handle(ReplicationMessage.DropSubscription message) {
			if (IsLegitimateReplicationMessage(message)) {
				Log.Information(
					"========== [{httpEndPoint}] DROP SUBSCRIPTION REQUEST RECEIVED FROM [{internalTcp},{internalSecureTcp},{leaderId:B}]. THIS MEANS THAT THERE IS A SURPLUS OF NODES IN THE CLUSTER, SHUTTING DOWN.",
					_nodeInfo.HttpEndPoint,
					_leader.InternalTcpEndPoint == null ? "n/a" : _leader.InternalTcpEndPoint.ToString(),
					_leader.InternalSecureTcpEndPoint == null ? "n/a" : _leader.InternalSecureTcpEndPoint.ToString(),
					message.LeaderId);
				_fsm.Handle(new ClientMessage.RequestShutdown(exitProcess: true, shutdownHttp: true));
			}
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

		private void Handle(ClientMessage.RequestShutdown message) {
			_outputBus.Publish(message);
			_fsm.Handle(
				new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), message.ExitProcess, message.ShutdownHttp));
		}

		private void Handle(SystemMessage.ServiceShutdown message) {
			Log.Information("========== [{httpEndPoint}] Service '{service}' has shut down.", _nodeInfo.HttpEndPoint,
				message.ServiceName);

			_serviceShutdownsToExpect -= 1;
			if (_serviceShutdownsToExpect == 0) {
				Log.Information("========== [{httpEndPoint}] All Services Shutdown.", _nodeInfo.HttpEndPoint);
				Shutdown();
			}

			_outputBus.Publish(message);
		}

		private void Handle(SystemMessage.ShutdownTimeout message) {
			Debug.Assert(State == VNodeState.ShuttingDown);

			Log.Error("========== [{httpEndPoint}] Shutdown Timeout.", _nodeInfo.HttpEndPoint);
			Shutdown();
			_outputBus.Publish(message);
		}

		private void Shutdown() {
			Debug.Assert(State == VNodeState.ShuttingDown);

			_db.Close();
			_fsm.Handle(new SystemMessage.BecomeShutdown(_stateCorrelationId));
		}
	}
}
