// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Storage.EpochManager;
using EventStore.Core.Services.TimerService;
using EventStore.Core.TransactionLog.Checkpoint;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services;

public enum ElectionsState {
	Idle,
	ElectingLeader,
	Leader,
	Acceptor,
	Shutdown
}

public class ElectionsService : IHandle<SystemMessage.BecomeShuttingDown>,
	IHandle<SystemMessage.SystemInit>,
	IHandle<LeaderDiscoveryMessage.LeaderFound>,
	IHandle<GossipMessage.GossipUpdated>,
	IHandle<ElectionMessage.StartElections>,
	IHandle<ElectionMessage.ElectionsTimedOut>,
	IHandle<ElectionMessage.ViewChange>,
	IHandle<ElectionMessage.ViewChangeProof>,
	IHandle<ElectionMessage.SendViewChangeProof>,
	IHandle<ElectionMessage.Prepare>,
	IHandle<ElectionMessage.PrepareOk>,
	IHandle<ElectionMessage.Proposal>,
	IHandle<ElectionMessage.Accept>,
	IHandle<ClientMessage.SetNodePriority>,
	IHandle<ClientMessage.ResignNode>,
	IHandle<ElectionMessage.LeaderIsResigning>,
	IHandle<ElectionMessage.LeaderIsResigningOk> {
	public static readonly TimeSpan SendViewChangeProofInterval = TimeSpan.FromMilliseconds(5000);
	private readonly TimeSpan _leaderElectionProgressTimeout;

	private static readonly ILogger Log = Serilog.Log.ForContext<ElectionsService>();
	private static readonly EndPointComparer IPComparer = new EndPointComparer();

	private readonly IPublisher _publisher;
	private readonly IEnvelope _publisherEnvelope;
	private readonly MemberInfo _memberInfo;
	private readonly int _clusterSize;
	private readonly IReadOnlyCheckpoint _writerCheckpoint;
	private readonly IReadOnlyCheckpoint _chaserCheckpoint;
	private readonly ICheckpoint _proposalCheckpoint;
	private readonly IEpochManager _epochManager;
	private readonly Func<long> _getLastCommitPosition;
	private int _nodePriority;
	private readonly ITimeProvider _timeProvider;

	private int _lastAttemptedView = -1;
	private int _lastInstalledView = -1;
	private ElectionsState _state = ElectionsState.Idle;

	private readonly HashSet<Guid> _vcReceived = [];
	private readonly Dictionary<Guid, ElectionMessage.PrepareOk> _prepareOkReceived = new();
	private readonly HashSet<Guid> _leaderIsResigningOkReceived = [];
	private readonly HashSet<Guid> _acceptsReceived = [];

	private LeaderCandidate _leaderProposal;
	public Guid LeaderProposalId => _leaderProposal?.InstanceId ?? Guid.Empty;
	private Guid? _leader;
	private Guid? _lastElectedLeader;

	private MemberInfo[] _servers;
	private Guid? _resigningLeaderInstanceId;

	public ElectionsService(IPublisher publisher,
		MemberInfo memberInfo,
		int clusterSize,
		IReadOnlyCheckpoint writerCheckpoint,
		IReadOnlyCheckpoint chaserCheckpoint,
		ICheckpoint proposalCheckpoint,
		IEpochManager epochManager,
		Func<long> getLastCommitPosition,
		int nodePriority,
		ITimeProvider timeProvider,
		TimeSpan leaderElectionTimeout) {
		if (leaderElectionTimeout.Seconds < 1) {
			throw new ArgumentOutOfRangeException(nameof(leaderElectionTimeout),
				$"{nameof(leaderElectionTimeout)} should be greater than 1 second.");
		}

		if (memberInfo.IsReadOnlyReplica) {
			throw new ArgumentException("Read-only replicas are not allowed to run the Elections service.");
		}

		_publisher = Ensure.NotNull(publisher);
		_memberInfo = Ensure.NotNull(memberInfo);
		_publisherEnvelope = publisher;
		_clusterSize = Ensure.Positive(clusterSize);
		_writerCheckpoint = Ensure.NotNull(writerCheckpoint);
		_chaserCheckpoint = Ensure.NotNull(chaserCheckpoint);
		_proposalCheckpoint = Ensure.NotNull(proposalCheckpoint);
		_epochManager = Ensure.NotNull(epochManager);
		_getLastCommitPosition = Ensure.NotNull(getLastCommitPosition);
		_nodePriority = nodePriority;
		_timeProvider = Ensure.NotNull(timeProvider);
		_leaderElectionProgressTimeout = leaderElectionTimeout;

		var lastEpoch = _epochManager.LastEpochNumber;
		if (_proposalCheckpoint.Read() < lastEpoch) {
			_proposalCheckpoint.Write(lastEpoch);
			_proposalCheckpoint.Flush();
		}

		var ownInfo = GetOwnInfo();
		_servers = [
			MemberInfo.ForVNode(memberInfo.InstanceId,
				_timeProvider.UtcNow,
				VNodeState.Initializing,
				true,
				memberInfo.InternalTcpEndPoint, memberInfo.InternalSecureTcpEndPoint,
				memberInfo.ExternalTcpEndPoint, memberInfo.ExternalSecureTcpEndPoint,
				memberInfo.HttpEndPoint,
				memberInfo.AdvertiseHostToClientAs, memberInfo.AdvertiseHttpPortToClientAs, memberInfo.AdvertiseTcpPortToClientAs,
				ownInfo.LastCommitPosition, ownInfo.WriterCheckpoint, ownInfo.ChaserCheckpoint,
				ownInfo.EpochPosition, ownInfo.EpochNumber, ownInfo.EpochId, ownInfo.NodePriority,
				memberInfo.IsReadOnlyReplica, VersionInfo.Version)
		];
	}

	public void SubscribeMessages(ISubscriber subscriber) {
		subscriber.Subscribe<SystemMessage.BecomeShuttingDown>(this);
		subscriber.Subscribe<SystemMessage.SystemInit>(this);
		subscriber.Subscribe<LeaderDiscoveryMessage.LeaderFound>(this);
		subscriber.Subscribe<GossipMessage.GossipUpdated>(this);
		subscriber.Subscribe<ElectionMessage.StartElections>(this);
		subscriber.Subscribe<ElectionMessage.ElectionsTimedOut>(this);
		subscriber.Subscribe<ElectionMessage.ViewChange>(this);
		subscriber.Subscribe<ElectionMessage.ViewChangeProof>(this);
		subscriber.Subscribe<ElectionMessage.SendViewChangeProof>(this);
		subscriber.Subscribe<ElectionMessage.Prepare>(this);
		subscriber.Subscribe<ElectionMessage.PrepareOk>(this);
		subscriber.Subscribe<ElectionMessage.Proposal>(this);
		subscriber.Subscribe<ElectionMessage.Accept>(this);
		subscriber.Subscribe<ElectionMessage.LeaderIsResigning>(this);
		subscriber.Subscribe<ElectionMessage.LeaderIsResigningOk>(this);
		subscriber.Subscribe<ClientMessage.SetNodePriority>(this);
		subscriber.Subscribe<ClientMessage.ResignNode>(this);
	}

	public void Handle(SystemMessage.SystemInit _) {
		_publisher.Publish(TimerMessage.Schedule.Create(SendViewChangeProofInterval,
			_publisherEnvelope,
			new ElectionMessage.SendViewChangeProof()));
	}

	public void Handle(ClientMessage.SetNodePriority message) {
		Log.Information("Setting Node Priority to {nodePriority}.", message.NodePriority);
		_nodePriority = message.NodePriority;
		_publisher.Publish(new GossipMessage.UpdateNodePriority(_nodePriority));
	}

	public void Handle(ClientMessage.ResignNode message) {
		if (_leader != null && _memberInfo.InstanceId == _leader) {
			_resigningLeaderInstanceId = _leader;
			var leaderIsResigningMessageOk = new ElectionMessage.LeaderIsResigningOk(
				_memberInfo.InstanceId,
				_memberInfo.HttpEndPoint,
				_memberInfo.InstanceId,
				_memberInfo.HttpEndPoint);
			_leaderIsResigningOkReceived.Clear();
			Handle(leaderIsResigningMessageOk);
			SendToAllExceptMe(new ElectionMessage.LeaderIsResigning(
				_memberInfo.InstanceId, _memberInfo.HttpEndPoint));
		} else {
			Log.Information("ELECTIONS: ONLY LEADER RESIGNATION IS SUPPORTED AT THE MOMENT. IGNORING RESIGNATION.");
		}
	}

	public void Handle(ElectionMessage.LeaderIsResigning message) {
		Log.Information("ELECTIONS: LEADER IS RESIGNING [{leaderHttpEndPoint}, {leaderId:B}].",
			message.LeaderHttpEndPoint, message.LeaderId);
		var leaderIsResigningMessageOk = new ElectionMessage.LeaderIsResigningOk(
			message.LeaderId,
			message.LeaderHttpEndPoint,
			_memberInfo.InstanceId,
			_memberInfo.HttpEndPoint);

		_resigningLeaderInstanceId = message.LeaderId;
		_publisher.Publish(new GrpcMessage.SendOverGrpc(message.LeaderHttpEndPoint, leaderIsResigningMessageOk,
			_timeProvider.LocalTime.Add(_leaderElectionProgressTimeout)));
	}

	public void Handle(ElectionMessage.LeaderIsResigningOk message) {
		Log.Information(
			"ELECTIONS: LEADER IS RESIGNING OK FROM [{serverHttpEndPoint},{serverId:B}] M=[{leaderHttpEndPoint},{leaderId:B}]).",
			message.ServerHttpEndPoint,
			message.ServerId,
			message.LeaderHttpEndPoint,
			message.LeaderId);
		if (_leaderIsResigningOkReceived.Add(message.ServerId) &&
		    _leaderIsResigningOkReceived.Count == _clusterSize / 2 + 1) {
			Log.Information(
				"ELECTIONS: MAJORITY OF ACCEPTANCE OF RESIGNATION OF LEADER [{leaderHttpEndPoint},{leaderId:B}]. NOW INITIATING LEADER RESIGNATION.",
				message.LeaderHttpEndPoint, message.LeaderId);
			_publisher.Publish(new SystemMessage.InitiateLeaderResignation());
		}
	}

	public void Handle(SystemMessage.BecomeShuttingDown message) {
		_state = ElectionsState.Shutdown;
	}

	public void Handle(GossipMessage.GossipUpdated message) {
		_servers = message.ClusterInfo.Members.Where(x => x.State != VNodeState.Manager)
			.Where(x => x.IsAlive)
			.OrderByDescending(x => x.HttpEndPoint, IPComparer)
			.ToArray();
	}

	public void Handle(ElectionMessage.StartElections message) {
		switch (_state) {
			case ElectionsState.Shutdown:
			case ElectionsState.ElectingLeader:
				return;
			default:
				Log.Information("ELECTIONS: STARTING ELECTIONS.");
				ShiftToLeaderElection(_lastAttemptedView + 1);
				break;
		}
	}

	public void Handle(ElectionMessage.ElectionsTimedOut message) {
		if (_state == ElectionsState.Shutdown)
			return;
		if (message.View != _lastAttemptedView)
			return;
		// we are still on the same view, but we selected leader
		if (_state != ElectionsState.ElectingLeader && _leader != null)
			return;

		Log.Information("ELECTIONS: (V={view}) TIMED OUT! (S={state}, M={leader}).", message.View, _state, _leader);
		ShiftToLeaderElection(_lastAttemptedView + 1);
	}

	private void ShiftToLeaderElection(int view) {
		Log.Information("ELECTIONS: (V={view}) SHIFT TO LEADER ELECTION.", view);

		_state = ElectionsState.ElectingLeader;
		_vcReceived.Clear();
		_prepareOkReceived.Clear();
		_lastAttemptedView = view;

		_leaderProposal = null;
		_leader = null;
		_acceptsReceived.Clear();

		var viewChangeMsg = new ElectionMessage.ViewChange(_memberInfo.InstanceId, _memberInfo.HttpEndPoint, view);
		Handle(viewChangeMsg);
		SendToAllExceptMe(viewChangeMsg);
		_publisher.Publish(TimerMessage.Schedule.Create(_leaderElectionProgressTimeout,
			_publisherEnvelope,
			new ElectionMessage.ElectionsTimedOut(view)));
	}

	private void SendToAllExceptMe(Message message) {
		foreach (var server in _servers.Where(x => x.InstanceId != _memberInfo.InstanceId)) {
			_publisher.Publish(new GrpcMessage.SendOverGrpc(server.HttpEndPoint, message,
				_timeProvider.LocalTime.Add(_leaderElectionProgressTimeout)));
		}
	}

	public void Handle(ElectionMessage.ViewChange message) {
		if (_state is ElectionsState.Shutdown or ElectionsState.Idle)
			return;

		if (message.AttemptedView <= _lastInstalledView)
			return;

		Log.Information("ELECTIONS: (V={view}) VIEWCHANGE FROM [{serverHttpEndPoint}, {serverId:B}].",
			message.AttemptedView, message.ServerHttpEndPoint, message.ServerId);

		if (message.AttemptedView > _lastAttemptedView)
			ShiftToLeaderElection(message.AttemptedView);

		if (_vcReceived.Add(message.ServerId) && _vcReceived.Count == _clusterSize / 2 + 1) {
			Log.Information("ELECTIONS: (V={view}) MAJORITY OF VIEWCHANGE.", message.AttemptedView);
			if (AmILeaderOf(_lastAttemptedView))
				ShiftToPreparePhase();
		}
	}

	public void Handle(ElectionMessage.SendViewChangeProof message) {
		if (_state == ElectionsState.Shutdown)
			return;

		if (_lastInstalledView >= 0)
			SendToAllExceptMe(new ElectionMessage.ViewChangeProof(_memberInfo.InstanceId, _memberInfo.HttpEndPoint, _lastInstalledView));

		_publisher.Publish(TimerMessage.Schedule.Create(SendViewChangeProofInterval, _publisherEnvelope, new ElectionMessage.SendViewChangeProof()));
	}

	public void Handle(ElectionMessage.ViewChangeProof message) {
		if (_state is ElectionsState.Shutdown or ElectionsState.Idle)
			return;
		if (message.InstalledView <= _lastInstalledView)
			return;

		_lastAttemptedView = message.InstalledView;

		_publisher.Publish(TimerMessage.Schedule.Create(_leaderElectionProgressTimeout,
			_publisherEnvelope,
			new ElectionMessage.ElectionsTimedOut(_lastAttemptedView)));

		if (AmILeaderOf(_lastAttemptedView)) {
			Log.Information(
				"ELECTIONS: (IV={installedView}) VIEWCHANGEPROOF FROM [{serverHttpEndPoint}, {serverId:B}]. JUMPING TO LEADER STATE.",
				message.InstalledView, message.ServerHttpEndPoint, message.ServerId);

			ShiftToPreparePhase();
		} else {
			Log.Information(
				"ELECTIONS: (IV={installedView}) VIEWCHANGEPROOF FROM [{serverHttpEndPoint}, {serverId:B}]. JUMPING TO NON-LEADER STATE.",
				message.InstalledView, message.ServerHttpEndPoint, message.ServerId);

			ShiftToAcceptor();
		}
	}

	private bool AmILeaderOf(int lastAttemptedView) {
		var serversExcludingNonPotentialLeaders = _servers.Where(x => !x.IsReadOnlyReplica).ToArray();
		var leader =
			serversExcludingNonPotentialLeaders[lastAttemptedView % serversExcludingNonPotentialLeaders.Length];
		return leader.InstanceId == _memberInfo.InstanceId;
	}

	private void ShiftToPreparePhase() {
		Log.Information("ELECTIONS: (V={lastAttemptedView}) SHIFT TO PREPARE PHASE.", _lastAttemptedView);

		_lastInstalledView = _lastAttemptedView;
		_prepareOkReceived.Clear();

		Handle(CreatePrepareOk(_lastInstalledView));
		SendToAllExceptMe(new ElectionMessage.Prepare(_memberInfo.InstanceId, _memberInfo.HttpEndPoint, _lastInstalledView));
	}

	public void Handle(ElectionMessage.Prepare message) {
		if (_state == ElectionsState.Shutdown)
			return;
		if (message.ServerId == _memberInfo.InstanceId)
			return;
		if (message.View != _lastAttemptedView)
			return;
		if (_servers.All(x => x.InstanceId != message.ServerId))
			return; // unknown instance

		Log.Information("ELECTIONS: (V={lastAttemptedView}) PREPARE FROM [{serverHttpEndPoint}, {serverId:B}].",
			_lastAttemptedView, message.ServerHttpEndPoint, message.ServerId);

		if (_state == ElectionsState.ElectingLeader) // install the view
			ShiftToAcceptor();

		var prepareOk = CreatePrepareOk(message.View);
		_publisher.Publish(new GrpcMessage.SendOverGrpc(message.ServerHttpEndPoint, prepareOk,
			_timeProvider.LocalTime.Add(_leaderElectionProgressTimeout)));
	}

	private ElectionMessage.PrepareOk CreatePrepareOk(int view) {
		var ownInfo = GetOwnInfo();
		var clusterInfo = new ClusterInfo(_servers);
		return new ElectionMessage.PrepareOk(view, ownInfo.InstanceId, ownInfo.HttpEndPoint,
			ownInfo.EpochNumber, ownInfo.EpochPosition, ownInfo.EpochId, ownInfo.EpochLeaderInstanceId,
			ownInfo.LastCommitPosition, ownInfo.WriterCheckpoint, ownInfo.ChaserCheckpoint,
			ownInfo.NodePriority, clusterInfo);
	}

	private void ShiftToAcceptor() {
		Log.Information("ELECTIONS: (V={lastAttemptedView}) SHIFT TO REG_ACCEPTOR.", _lastAttemptedView);

		_state = ElectionsState.Acceptor;
		_lastInstalledView = _lastAttemptedView;
	}

	public void Handle(ElectionMessage.PrepareOk msg) {
		if (_state is ElectionsState.Shutdown or not ElectionsState.ElectingLeader)
			return;
		if (msg.View != _lastAttemptedView)
			return;

		Log.Information("ELECTIONS: (V={view}) PREPARE_OK FROM {nodeInfo}.", msg.View,
			FormatNodeInfo(msg.ServerHttpEndPoint, msg.ServerId,
				msg.LastCommitPosition, msg.WriterCheckpoint, msg.ChaserCheckpoint,
				msg.EpochNumber, msg.EpochPosition, msg.EpochId, msg.EpochLeaderInstanceId, msg.NodePriority));

		if (_prepareOkReceived.TryAdd(msg.ServerId, msg)) {
			if (_prepareOkReceived.Count == _clusterSize / 2 + 1)
				ShiftToLeader();
		}
	}

	private void ShiftToLeader() {
		Log.Information("ELECTIONS: (V={lastAttemptedView}) SHIFT TO REG_LEADER.", _lastAttemptedView);

		_state = ElectionsState.Leader;
		SendProposal();
	}

	private void SendProposal() {
		_acceptsReceived.Clear();
		_leaderProposal = null;

		var leader = GetBestLeaderCandidate(_prepareOkReceived, _servers, _resigningLeaderInstanceId, _lastAttemptedView);
		if (leader == null) {
			Log.Information("ELECTIONS: (V={lastAttemptedView}) NO LEADER CANDIDATE WHEN TRYING TO SEND PROPOSAL.", _lastAttemptedView);
			return;
		}

		var proposalNumber = Math.Max(_epochManager.LastEpochNumber, _proposalCheckpoint.Read()) + 1;
		_proposalCheckpoint.Write(proposalNumber);
		_proposalCheckpoint.Flush();
		leader.ProposalNumber = (int)proposalNumber;
		_leaderProposal = leader;

		Log.Information("ELECTIONS: (V={lastAttemptedView}) SENDING PROPOSAL CANDIDATE: {formatNodeInfo}, PROPOSAL NUMBER {proposal}, ME: {ownInfo}.",
			_lastAttemptedView, FormatNodeInfo(leader), proposalNumber, FormatNodeInfo(GetOwnInfo()));

		var proposal = new ElectionMessage.Proposal(_memberInfo.InstanceId, _memberInfo.HttpEndPoint,
			leader.InstanceId, leader.HttpEndPoint,
			_lastInstalledView,
			(int)proposalNumber, leader.EpochPosition, leader.EpochId, leader.EpochLeaderInstanceId,
			leader.LastCommitPosition, leader.WriterCheckpoint, leader.ChaserCheckpoint, leader.NodePriority);
		Handle(new ElectionMessage.Accept(_memberInfo.InstanceId, _memberInfo.HttpEndPoint,
			leader.InstanceId, leader.HttpEndPoint, _lastInstalledView));
		SendToAllExceptMe(proposal);
	}

	public static LeaderCandidate GetBestLeaderCandidate(Dictionary<Guid, ElectionMessage.PrepareOk> received,
		MemberInfo[] servers, Guid? resigningLeaderInstanceId, int lastAttemptedView) {
		var best = received.Values
			.OrderByDescending(x => x.EpochNumber) // latest election
			.ThenByDescending(x => x.WriterCheckpoint) // log length (if not re-electing leader pick the most complete replica)
			.ThenByDescending(x => x.ChaserCheckpoint) // in memory index position
			.ThenBy(x => x.ServerId == resigningLeaderInstanceId) // pick other nodes over a resigning leader
			.ThenByDescending(x => x.NodePriority) // indicated node priority from settings
			.ThenByDescending(x => x.ServerId) // tie breaker
			.FirstOrDefault();

		if (best == null)
			return null;

		var bestCandidate = new LeaderCandidate(best.ServerId, best.ServerHttpEndPoint,
			best.EpochNumber, best.EpochPosition, best.EpochId, best.EpochLeaderInstanceId,
			best.LastCommitPosition, best.WriterCheckpoint, best.ChaserCheckpoint, best.NodePriority);

		if (best.EpochLeaderInstanceId != Guid.Empty) {
			//best.EpochLeaderInstanceId = id of the last leader/master which has been able to write an epoch record to the transaction log and get it replicated
			//to the "best" node which has the latest data, i.e data that has been replicated to at least a quorum number of nodes for sure.
			//We know that the data from the "best" node originates from this leader/master itself from this epoch record onwards, thus it implies that
			//the leader/master in this epoch record must have the latest replicated data as well and is thus definitely a valid candidate for leader/master if it's still alive.
			//NOTE 1: It does not matter if the "best" node's latest epoch record has been replicated to a quorum number of nodes or not, the
			//leader/master in the epoch record must still have the latest replicated data
			//NOTE 2: it is not necessary that this leader/master is the last elected leader/master in the cluster e.g a leader/master might be elected but
			//has no time to replicate the epoch. In this case, it will need to truncate when joining the new leader.

			var previousLeaderId = best.EpochLeaderInstanceId;
			LeaderCandidate previousLeaderCandidate = null;

			//we have received a PrepareOk message from the previous leader, so we definitely know it's alive.
			if (received.TryGetValue(previousLeaderId, out var leaderMsg) &&
			    leaderMsg.EpochNumber == best.EpochNumber &&
			    leaderMsg.EpochId == best.EpochId) {
				Log.Debug("ELECTIONS: (V={lastAttemptedView}) Previous Leader (L={previousLeaderId:B}) from last epoch record is still alive.", lastAttemptedView, previousLeaderId);
				previousLeaderCandidate = new LeaderCandidate(leaderMsg.ServerId, leaderMsg.ServerHttpEndPoint,
					leaderMsg.EpochNumber, leaderMsg.EpochPosition, leaderMsg.EpochId, leaderMsg.EpochLeaderInstanceId,
					leaderMsg.LastCommitPosition, leaderMsg.WriterCheckpoint, leaderMsg.ChaserCheckpoint,
					leaderMsg.NodePriority);
			}

			//we try to find at least one node from the quorum no. of PrepareOk messages which says that the previous leader is alive
			//if none of them say it's alive, a quorum can very probably not be formed with the previous leader
			//otherwise we know that the leader was alive during the last gossip time interval and we try our luck
			if (previousLeaderCandidate == null) {
				foreach (var (id, prepareOk) in received) {
					var member = prepareOk.ClusterInfo.Members.FirstOrDefault(x => x.InstanceId == previousLeaderId && x.IsAlive);

					if (member != null) {
						//these checks are not really necessary but we do them to ensure that everything is normal.
						if (best.EpochNumber == member.EpochNumber && best.EpochId != member.EpochId) {
							//something is definitely not right if the epoch numbers match but not the epoch ids
							Log.Warning(
								"ELECTIONS: (V={lastAttemptedView}) Epoch ID mismatch in gossip information from node {nodeId:B}. Best node's Epoch Id: {bestEpochId:B}, Leader node's Epoch Id: {leaderEpochId:B}",
								lastAttemptedView, id, best.EpochId, member.EpochId);
							continue;
						}

						if (best.EpochNumber - member.EpochNumber > 2) {
							//gossip information may be slightly out of date. We log a warning if the epoch number is off by more than 2
							Log.Warning(
								"ELECTIONS: (V={lastAttemptedView}) Epoch number is off by more than two in gossip information from node {nodeId:B}. Best node's Epoch number: {bestEpochNumber}, Leader node's Epoch number: {leaderEpochNumber}.",
								lastAttemptedView, id, best.EpochNumber, member.EpochNumber);
						}

						Log.Debug("ELECTIONS: (V={lastAttemptedView}) Previous Leader (L={previousLeaderId:B}) from last epoch record is still alive according to gossip from node {nodeId:B}.",
							lastAttemptedView, previousLeaderId, id);
						previousLeaderCandidate = new LeaderCandidate(member.InstanceId,
							member.HttpEndPoint,
							member.EpochNumber, member.EpochPosition, member.EpochId, best.EpochLeaderInstanceId,
							member.LastCommitPosition, member.WriterCheckpoint, member.ChaserCheckpoint,
							member.NodePriority);
						break;
					}
				}
			}

			if (previousLeaderCandidate == null) {
				Log.Debug("ELECTIONS: (V={lastAttemptedView}) Previous Leader (L={previousLeaderId:B}) from last epoch record is dead, defaulting to the best candidate (B={bestCandidateId:B}).",
					lastAttemptedView, previousLeaderId, bestCandidate.InstanceId);
			} else if (previousLeaderCandidate.InstanceId == resigningLeaderInstanceId) {
				Log.Debug(
					"ELECTIONS: (V={lastAttemptedView}) Previous Leader (L={previousLeaderId:B}) from last epoch record is alive but it is resigning, defaulting to the best candidate (B={bestCandidateId:B}).",
					lastAttemptedView, previousLeaderId, bestCandidate.InstanceId);
			} else {
				bestCandidate = previousLeaderCandidate;
			}
		}

		Log.Debug("ELECTIONS: (V={lastAttemptedView}) Proposing node: {leaderCandidateId:B} as best leader candidate", lastAttemptedView, bestCandidate.InstanceId);
		return bestCandidate;
	}

	public static bool IsLegitimateLeader(int view, EndPoint proposingServerEndPoint, Guid proposingServerId,
		LeaderCandidate candidate, MemberInfo[] servers, Guid? lastElectedLeader, Guid instanceId,
		LeaderCandidate ownInfo,
		Guid? resigningLeader) {
		var leader = servers.FirstOrDefault(x =>
			x.IsAlive && x.InstanceId == lastElectedLeader && x.State == VNodeState.Leader);

		if (leader != null && leader.InstanceId != resigningLeader) {
			if (candidate.InstanceId == leader.InstanceId
			    || candidate.EpochNumber > leader.EpochNumber
			    || (candidate.EpochNumber == leader.EpochNumber && candidate.EpochId != leader.EpochId))
				return true;

			Log.Information(
				"ELECTIONS: (V={view}) NOT LEGITIMATE LEADER PROPOSAL FROM [{proposingServerEndPoint},{proposingServerId:B}] M={candidateInfo}. "
				+ "PREVIOUS LEADER IS ALIVE: [{leaderHttpEndPoint},{leaderId:B}].",
				view, proposingServerEndPoint, proposingServerId, FormatNodeInfo(candidate),
				leader.HttpEndPoint, leader.InstanceId);
			return false;
		}

		if (candidate.InstanceId == instanceId)
			return true;

		if (!IsCandidateGoodEnough(candidate, ownInfo)) {
			Log.Information(
				"ELECTIONS: (V={view}) NOT LEGITIMATE LEADER PROPOSAL FROM [{proposingServerEndPoint},{proposingServerId:B}] M={candidateInfo}. ME={ownInfo}.",
				view, proposingServerEndPoint, proposingServerId, FormatNodeInfo(candidate),
				FormatNodeInfo(ownInfo));
			return false;
		}

		return true;
	}

	private static bool IsCandidateGoodEnough(LeaderCandidate candidate, LeaderCandidate ownInfo) {
		if (candidate.EpochNumber != ownInfo.EpochNumber)
			return candidate.EpochNumber > ownInfo.EpochNumber;
		if (candidate.LastCommitPosition != ownInfo.LastCommitPosition)
			return candidate.LastCommitPosition > ownInfo.LastCommitPosition;
		if (candidate.WriterCheckpoint != ownInfo.WriterCheckpoint)
			return candidate.WriterCheckpoint > ownInfo.WriterCheckpoint;
		if (candidate.ChaserCheckpoint != ownInfo.ChaserCheckpoint)
			return candidate.ChaserCheckpoint > ownInfo.ChaserCheckpoint;
		return true;
	}

	public void Handle(ElectionMessage.Proposal message) {
		if (_state is ElectionsState.Shutdown or not ElectionsState.Acceptor)
			return;
		if (message.ServerId == _memberInfo.InstanceId)
			return;
		if (message.View != _lastInstalledView)
			return;
		if (_servers.All(x => x.InstanceId != message.ServerId))
			return;
		if (_servers.All(x => x.InstanceId != message.LeaderId))
			return;

		var candidate = new LeaderCandidate(message.LeaderId, message.LeaderHttpEndPoint,
			message.EpochNumber, message.EpochPosition, message.EpochId, message.EpochLeaderInstanceId,
			message.LastCommitPosition, message.WriterCheckpoint, message.ChaserCheckpoint, message.NodePriority);

		if (message.EpochNumber > _proposalCheckpoint.Read()) {
			_proposalCheckpoint.Write(message.EpochNumber);
			_proposalCheckpoint.Flush();
		} else {
			Log.Information(
				"ELECTIONS: (V={lastAttemptedView}, P={lastKnownProposal}) OUTDATED PROPOSAL P={proposedEpoch} FROM [{serverHttpEndPoint},{serverId:B}] M={candidateInfo}. ME={ownInfo}, NodePriority={priority}",
				_lastAttemptedView,
				_proposalCheckpoint.Read(),
				message.EpochNumber,
				message.ServerHttpEndPoint, message.ServerId, FormatNodeInfo(candidate), FormatNodeInfo(GetOwnInfo()),
				message.NodePriority);
			return;
		}

		var ownInfo = GetOwnInfo();
		if (!IsLegitimateLeader(message.View, message.ServerHttpEndPoint, message.ServerId,
			    candidate, _servers, _lastElectedLeader, _memberInfo.InstanceId, ownInfo,
			    _resigningLeaderInstanceId))
			return;

		Log.Information(
			"ELECTIONS: (V={lastAttemptedView}, P={lastKnownProposal}) PROPOSAL P={proposedEpoch} FROM [{serverHttpEndPoint},{serverId:B}] M={candidateInfo}. ME={ownInfo}, NodePriority={priority}",
			_lastAttemptedView,
			_proposalCheckpoint.Read(),
			message.EpochNumber,
			message.ServerHttpEndPoint, message.ServerId, FormatNodeInfo(candidate), FormatNodeInfo(GetOwnInfo()),
			message.NodePriority);

		if (_leaderProposal == null) {
			_leaderProposal = candidate;
			_leaderProposal.ProposalNumber = message.EpochNumber;
			_acceptsReceived.Clear();
		}

		if (_leaderProposal.InstanceId == message.LeaderId) {
			// NOTE: proposal from other server is also implicit Accept from that server
			Handle(new ElectionMessage.Accept(message.ServerId, message.ServerHttpEndPoint, message.LeaderId, message.LeaderHttpEndPoint, message.View));
			var accept = new ElectionMessage.Accept(_memberInfo.InstanceId, _memberInfo.HttpEndPoint, message.LeaderId, message.LeaderHttpEndPoint, message.View);
			Handle(accept); // implicitly sent accept to ourselves
			SendToAllExceptMe(accept);
		}
	}

	public void Handle(ElectionMessage.Accept message) {
		if (_state == ElectionsState.Shutdown)
			return;
		if (message.View != _lastInstalledView)
			return;
		if (_leaderProposal == null)
			return;
		if (_leaderProposal.InstanceId != message.LeaderId)
			return;

		Log.Information(
			"ELECTIONS: (V={view}) ACCEPT FROM [{serverHttpEndPoint},{serverId:B}] M=[{leaderHttpEndPoint},{leaderId:B}]).",
			message.View,
			message.ServerHttpEndPoint, message.ServerId, message.LeaderHttpEndPoint, message.LeaderId);

		if (_acceptsReceived.Add(message.ServerId) && _acceptsReceived.Count == _clusterSize / 2 + 1) {
			var leader = _servers.FirstOrDefault(x => x.InstanceId == _leaderProposal.InstanceId);
			if (leader != null) {
				_leader = _leaderProposal.InstanceId;
				Log.Information("ELECTIONS: (V={view}) DONE. ELECTED LEADER = {leaderInfo}. ME={ownInfo}.", message.View,
					FormatNodeInfo(_leaderProposal), FormatNodeInfo(GetOwnInfo()));
				_lastElectedLeader = _leader;
				_resigningLeaderInstanceId = null;
				_publisher.Publish(new ElectionMessage.ElectionsDone(message.View, _leaderProposal.ProposalNumber, leader));
			}
		}
	}

	public void Handle(LeaderDiscoveryMessage.LeaderFound message) {
		if (_leader != null || _lastElectedLeader != null || _state != ElectionsState.Idle)
			return;

		Log.Information("ELECTIONS: Existing LEADER was discovered, updating information. M=[{leaderHttpEndPoint},{leaderId:B}])", message.Leader.HttpEndPoint, message.Leader.InstanceId);
		_leader = message.Leader.InstanceId;
		_lastElectedLeader = message.Leader.InstanceId;
		_lastAttemptedView = 0;
		_lastInstalledView = 0;
		_state = ElectionsState.Acceptor;
	}

	private LeaderCandidate GetOwnInfo() {
		var lastEpoch = _epochManager.GetLastEpoch();
		var writerCheckpoint = _writerCheckpoint.Read();
		var chaserCheckpoint = _chaserCheckpoint.Read();
		var lastCommitPosition = _getLastCommitPosition();
		return new LeaderCandidate(_memberInfo.InstanceId, _memberInfo.HttpEndPoint,
			lastEpoch?.EpochNumber ?? -1,
			lastEpoch?.EpochPosition ?? -1,
			lastEpoch?.EpochId ?? Guid.Empty,
			lastEpoch?.LeaderInstanceId ?? Guid.Empty,
			lastCommitPosition, writerCheckpoint, chaserCheckpoint, _nodePriority);
	}

	private static string FormatNodeInfo(LeaderCandidate candidate) {
		return FormatNodeInfo(candidate.HttpEndPoint, candidate.InstanceId,
			candidate.LastCommitPosition, candidate.WriterCheckpoint, candidate.ChaserCheckpoint,
			candidate.EpochNumber, candidate.EpochPosition, candidate.EpochId, candidate.EpochLeaderInstanceId, candidate.NodePriority);
	}

	private static string FormatNodeInfo(EndPoint serverEndPoint, Guid serverId,
		long lastCommitPosition, long writerCheckpoint, long chaserCheckpoint,
		int epochNumber, long epochPosition, Guid epochId, Guid epochLeaderInstanceId, int priority) {
		return $"[{serverEndPoint},{serverId:B}](L={lastCommitPosition},W={writerCheckpoint},C={chaserCheckpoint},E{epochNumber}@{epochPosition}:{epochId:B} (L={epochLeaderInstanceId:B}),Priority={priority})";
	}

	public class LeaderCandidate(
		Guid instanceId,
		EndPoint httpEndPoint,
		int epochNumber,
		long epochPosition,
		Guid epochId,
		Guid epochLeaderInstanceId,
		long lastCommitPosition,
		long writerCheckpoint,
		long chaserCheckpoint,
		int nodePriority) {
		public readonly Guid InstanceId = instanceId;
		public readonly EndPoint HttpEndPoint = httpEndPoint;
		public int ProposalNumber;
		public readonly int EpochNumber = epochNumber;
		public readonly long EpochPosition = epochPosition;
		public readonly Guid EpochId = epochId;
		public readonly Guid EpochLeaderInstanceId = epochLeaderInstanceId;
		public readonly long LastCommitPosition = lastCommitPosition;
		public readonly long WriterCheckpoint = writerCheckpoint;
		public readonly long ChaserCheckpoint = chaserCheckpoint;
		public readonly int NodePriority = nodePriority;
	}
}
