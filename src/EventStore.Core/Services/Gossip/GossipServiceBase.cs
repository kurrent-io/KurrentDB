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
using EventStore.Core.Services.TimerService;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Gossip {
	public abstract class GossipServiceBase : IHandle<SystemMessage.SystemInit>,
		IHandle<GossipMessage.RetrieveGossipSeedSources>,
		IHandle<GossipMessage.GotGossipSeedSources>,
		IHandle<GossipMessage.Gossip>,
		IHandle<GossipMessage.GossipReceived>,
		IHandle<GossipMessage.ReadGossip>,
		IHandle<GossipMessage.ClientGossip>,
		IHandle<SystemMessage.StateChangeMessage>,
		IHandle<GossipMessage.GossipSendFailed>,
		IHandle<SystemMessage.VNodeConnectionLost>,
		IHandle<SystemMessage.VNodeConnectionEstablished>,
		IHandle<GossipMessage.GetGossipReceived>,
		IHandle<GossipMessage.GetGossipFailed>,
		IHandle<ElectionMessage.ElectionsDone> {
		public const int GossipRoundStartupThreshold = 20;
		public static readonly TimeSpan DnsRetryTimeout = TimeSpan.FromMilliseconds(1000);
		public static readonly TimeSpan GossipStartupInterval = TimeSpan.FromMilliseconds(100);
		private readonly TimeSpan DeadMemberRemovalPeriod;
		private static readonly ILogger Log = Serilog.Log.ForContext<GossipServiceBase>();

		protected readonly MemberInfo _memberInfo;
		protected VNodeState CurrentRole = VNodeState.Initializing;
		private MemberInfo CurrentLeader;
		private readonly TimeSpan GossipInterval;
		private readonly TimeSpan AllowedTimeDifference;
		private readonly TimeSpan GossipTimeout;

		private readonly IPublisher _bus;
		private readonly IEnvelope _publishEnvelope;
		private readonly IGossipSeedSource _gossipSeedSource;

		private GossipState _state;
		private ClusterInfo _cluster;
		private readonly Random _rnd = new Random(Math.Abs(Environment.TickCount));
		private readonly ITimeProvider _timeProvider;
		private readonly Func<MemberInfo[], MemberInfo> _getNodeToGossipTo;

		protected GossipServiceBase(IPublisher bus,
			IGossipSeedSource gossipSeedSource,
			MemberInfo memberInfo,
			TimeSpan gossipInterval,
			TimeSpan allowedTimeDifference,
			TimeSpan gossipTimeout,
			TimeSpan deadMemberRemovalPeriod,
			ITimeProvider timeProvider,
			Func<MemberInfo[], MemberInfo> getNodeToGossipTo = null) {
			Ensure.NotNull(bus, "bus");
			Ensure.NotNull(gossipSeedSource, "gossipSeedSource");
			Ensure.NotNull(memberInfo, "nodeInfo");
			Ensure.NotNull(timeProvider, nameof(timeProvider));

			_bus = bus;
			_publishEnvelope = new PublishEnvelope(bus);
			_gossipSeedSource = gossipSeedSource;
			_memberInfo = memberInfo;
			GossipInterval = gossipInterval;
			AllowedTimeDifference = allowedTimeDifference;
			GossipTimeout = gossipTimeout;
			DeadMemberRemovalPeriod = deadMemberRemovalPeriod;
			_state = GossipState.Startup;
			_timeProvider = timeProvider;
			_getNodeToGossipTo = getNodeToGossipTo ?? GetNodeToGossipTo;
		}

		protected abstract MemberInfo GetInitialMe();
		protected abstract MemberInfo GetUpdatedMe(MemberInfo me);

		public void Handle(SystemMessage.SystemInit message) {
			if (_state != GossipState.Startup)
				return;
			_cluster = new ClusterInfo(GetInitialMe());
			Handle(new GossipMessage.RetrieveGossipSeedSources());
		}

		public void Handle(GossipMessage.RetrieveGossipSeedSources message) {
			_state = GossipState.RetrievingGossipSeeds;
			try {
				_gossipSeedSource.BeginGetHostEndpoints(OnGotGossipSeedSources, null);
			} catch (Exception ex) {
				Log.Error(ex, "Error while retrieving cluster members through DNS.");
				_bus.Publish(TimerMessage.Schedule.Create(DnsRetryTimeout, _publishEnvelope,
					new GossipMessage.RetrieveGossipSeedSources()));
			}
		}

		private void OnGotGossipSeedSources(IAsyncResult ar) {
			try {
				var entries = _gossipSeedSource.EndGetHostEndpoints(ar);
				_bus.Publish(new GossipMessage.GotGossipSeedSources(entries));
			} catch (Exception ex) {
				Log.Error(ex, "Error while retrieving cluster members through DNS.");
				_bus.Publish(TimerMessage.Schedule.Create(DnsRetryTimeout, _publishEnvelope,
					new GossipMessage.RetrieveGossipSeedSources()));
			}
		}

		public void Handle(GossipMessage.GotGossipSeedSources message) {
			var now = _timeProvider.UtcNow;
			var dnsCluster = new ClusterInfo(
				message.GossipSeeds.Select(x => MemberInfo.ForManager(Guid.Empty, now, true, x)).ToArray());

			var oldCluster = _cluster;
			_cluster = MergeClusters(_cluster, dnsCluster, null, x => x, _timeProvider.UtcNow, _memberInfo,
				CurrentLeader?.InstanceId, AllowedTimeDifference, DeadMemberRemovalPeriod);

			LogClusterChange(oldCluster, _cluster, null);

			_state = GossipState.Working;
			Handle(new GossipMessage.Gossip(0));
		}

		public void Handle(GossipMessage.Gossip message) {
			if (_state != GossipState.Working)
				return;

			var node = _getNodeToGossipTo(_cluster.Members);
			if (node != null) {
				_cluster = UpdateCluster(_cluster, x => x.InstanceId == _memberInfo.InstanceId ? GetUpdatedMe(x) : x,
					_timeProvider, DeadMemberRemovalPeriod, CurrentRole);
				_bus.Publish(new GrpcMessage.SendOverGrpc(node.HttpEndPoint,
					new GossipMessage.SendGossip(_cluster, _memberInfo.HttpEndPoint),
					_timeProvider.LocalTime.Add(GossipTimeout)));
			}

			var interval = message.GossipRound < GossipRoundStartupThreshold ? GossipStartupInterval : GossipInterval;
			var gossipRound = Math.Min(int.MaxValue - 1, node == null ? message.GossipRound : message.GossipRound + 1);
			_bus.Publish(
				TimerMessage.Schedule.Create(interval, _publishEnvelope, new GossipMessage.Gossip(gossipRound)));
		}

		private MemberInfo GetNodeToGossipTo(MemberInfo[] members) {
			if (members.Length == 0)
				return null;
			for (int i = 0; i < 5; ++i) {
				var node = members[_rnd.Next(members.Length)];
				if (node.InstanceId != _memberInfo.InstanceId)
					return node;
			}

			return null;
		}

		public void Handle(GossipMessage.GossipReceived message) {
			if (_state != GossipState.Working)
				return;

			var oldCluster = _cluster;
			_cluster = MergeClusters(_cluster,
				message.ClusterInfo,
				message.Server,
				x => x.InstanceId == _memberInfo.InstanceId ? GetUpdatedMe(x) : x,
				_timeProvider.UtcNow, _memberInfo, CurrentLeader?.InstanceId, AllowedTimeDifference,
				DeadMemberRemovalPeriod);

			message.Envelope.ReplyWith(new GossipMessage.SendGossip(_cluster, _memberInfo.HttpEndPoint));

			if (_cluster.HasChangedSince(oldCluster))
				LogClusterChange(oldCluster, _cluster, $"gossip received from [{message.Server}]");
			_bus.Publish(new GossipMessage.GossipUpdated(_cluster));
		}

		public void Handle(GossipMessage.ReadGossip message) {
			if (_cluster != null) {
				message.Envelope.ReplyWith(new GossipMessage.SendGossip(_cluster, _memberInfo.HttpEndPoint));
			}
		}

		public void Handle(GossipMessage.ClientGossip message) {
			if (_cluster != null) {
				var advertisedAddress = string.IsNullOrEmpty(_memberInfo.AdvertiseHostToClientAs)
					? _memberInfo.HttpEndPoint.GetHost()
					: _memberInfo.AdvertiseHostToClientAs;
				var advertisedPort = _memberInfo.AdvertiseHttpPortToClientAs == 0
					? _memberInfo.HttpEndPoint.GetPort()
					: _memberInfo.AdvertiseHttpPortToClientAs;
				message.Envelope.ReplyWith(new GossipMessage.SendClientGossip(
					new ClientClusterInfo(_cluster, advertisedAddress, advertisedPort)));
			}
		}

		public void Handle(SystemMessage.StateChangeMessage message) {
			CurrentRole = message.State;
			var replicaState = message as SystemMessage.ReplicaStateMessage;
			CurrentLeader = replicaState == null ? null : replicaState.Leader;
			_cluster = UpdateCluster(_cluster, x => x.InstanceId == _memberInfo.InstanceId ? GetUpdatedMe(x) : x,
				_timeProvider, DeadMemberRemovalPeriod, CurrentRole);

			_bus.Publish(new GossipMessage.GossipUpdated(_cluster));
		}

		public void Handle(GossipMessage.GossipSendFailed message) {
			var node = _cluster.Members.FirstOrDefault(x => x.Is(message.Recipient));
			if (node == null || !node.IsAlive)
				return;

			if (node.InstanceId == CurrentLeader?.InstanceId) {
				Log.Information(
					"Leader [{leaderEndPoint}, {instanceId:B}] appears to be DEAD (Gossip send failed); wait for TCP to decide.",
					message.Recipient, node.InstanceId);
				return;
			}

			Log.Information("Looks like node [{nodeEndPoint}] is DEAD (Gossip send failed).", message.Recipient);

			var oldCluster = _cluster;
			_cluster = UpdateCluster(_cluster, x => x.Is(message.Recipient)
					? x.Updated(_timeProvider.UtcNow, isAlive: false)
					: x,
				_timeProvider, DeadMemberRemovalPeriod, CurrentRole);
			if (_cluster.HasChangedSince(oldCluster))
				LogClusterChange(oldCluster, _cluster, $"gossip send failed to [{message.Recipient}]");
			_bus.Publish(new GossipMessage.GossipUpdated(_cluster));
		}

		public void Handle(SystemMessage.VNodeConnectionLost message) {
			var node = _cluster.Members.FirstOrDefault(x => x.Is(message.VNodeEndPoint));
			if (node == null || !node.IsAlive)
				return;

			Log.Information("Looks like node [{nodeEndPoint}] is DEAD (TCP connection lost). Issuing a gossip to confirm.",
				message.VNodeEndPoint);
			_bus.Publish(new GrpcMessage.SendOverGrpc(node.HttpEndPoint,
				new GossipMessage.GetGossip(),
				_timeProvider.LocalTime.Add(GossipTimeout)));
		}

		public void Handle(GossipMessage.GetGossipReceived message) {
			if (_state != GossipState.Working)
				return;

			Log.Information("Gossip Received, The node [{nodeEndpoint}] is not DEAD.", message.Server);

			var oldCluster = _cluster;
			_cluster = MergeClusters(_cluster,
				message.ClusterInfo,
				message.Server,
				x => x.InstanceId == _memberInfo.InstanceId ? GetUpdatedMe(x) : x,
				_timeProvider.UtcNow, _memberInfo, CurrentLeader?.InstanceId, AllowedTimeDifference,
				DeadMemberRemovalPeriod);

			if (_cluster.HasChangedSince(oldCluster))
				LogClusterChange(oldCluster, _cluster, string.Format("gossip received from [{0}]", message.Server));

			_bus.Publish(new GossipMessage.GossipUpdated(_cluster));
		}

		public void Handle(GossipMessage.GetGossipFailed message) {
			if (_state != GossipState.Working)
				return;

			Log.Information("Gossip Failed, The node [{nodeEndpoint}] is being marked as DEAD. Reason: {reason}",
				message.Recipient,
				message.Reason);

			var oldCluster = _cluster;
			_cluster = UpdateCluster(_cluster, x => x.Is(message.Recipient)
					? x.Updated(
						_timeProvider.UtcNow, isAlive: false)
					: x,
				_timeProvider, DeadMemberRemovalPeriod, CurrentRole);
			if (_cluster.HasChangedSince(oldCluster))
				LogClusterChange(oldCluster, _cluster,
					string.Format("TCP connection lost to [{0}]", message.Recipient));
			_bus.Publish(new GossipMessage.GossipUpdated(_cluster));
		}

		public void Handle(SystemMessage.VNodeConnectionEstablished message) {
			var oldCluster = _cluster;
			_cluster = UpdateCluster(_cluster, x => x.Is(message.VNodeEndPoint)
					? x.Updated(
						_timeProvider.UtcNow, isAlive: true)
					: x,
				_timeProvider, DeadMemberRemovalPeriod, CurrentRole);
			if (_cluster.HasChangedSince(oldCluster))
				LogClusterChange(oldCluster, _cluster,
					string.Format("TCP connection established to [{0}]", message.VNodeEndPoint));
			_bus.Publish(new GossipMessage.GossipUpdated(_cluster));
		}

		public void Handle(ElectionMessage.ElectionsDone message) {
			var oldCluster = _cluster;
			_cluster = UpdateCluster(_cluster,
				x => x.InstanceId == message.Leader.InstanceId
					? x.Updated(_timeProvider.UtcNow, VNodeState.Leader)
					: x.Updated(_timeProvider.UtcNow, VNodeState.Unknown),
				_timeProvider, DeadMemberRemovalPeriod, CurrentRole);
			if (_cluster.HasChangedSince(oldCluster))
				LogClusterChange(oldCluster, _cluster, "Elections Done");
			_bus.Publish(new GossipMessage.GossipUpdated(_cluster));
		}

		public static ClusterInfo MergeClusters(ClusterInfo myCluster, ClusterInfo othersCluster,
			EndPoint peerEndPoint, Func<MemberInfo, MemberInfo> update, DateTime utcNow,
			MemberInfo me, Guid? currentLeaderInstanceId, TimeSpan allowedTimeDifference,
			TimeSpan deadMemberRemovalTimeout) {
			var members = myCluster.Members.ToDictionary(member => member.HttpEndPoint,
				new EndPointEqualityComparer());
			MemberInfo peerNode = peerEndPoint != null
				? othersCluster.Members.SingleOrDefault(member => member.Is(peerEndPoint), null) : null;
			bool isPeerOld = peerNode?.ESVersion == null;
			foreach (var member in othersCluster.Members) {
				if (member.InstanceId == me.InstanceId || member.Is(me.HttpEndPoint)
				) // we know about ourselves better
					continue;
				if (member.Equals(peerNode)) // peer knows about itself better
				{
					if ((utcNow - member.TimeStamp).Duration() > allowedTimeDifference) {
						Log.Error("Time difference between us and [{peerEndPoint}] is too great! "
								  + "UTC now: {dateTime:yyyy-MM-dd HH:mm:ss.fff}, peer's time stamp: {peerTimestamp:yyyy-MM-dd HH:mm:ss.fff}.",
							peerEndPoint, utcNow, member.TimeStamp);
					}
					members[member.HttpEndPoint] = member.Updated(utcNow: member.TimeStamp, esVersion: isPeerOld ? VersionInfo.OldVersion : member.ESVersion);
				} else {
					MemberInfo existingMem;
					// if there is no data about this member or data is stale -- update
					if (!members.TryGetValue(member.HttpEndPoint, out existingMem) ||
						IsMoreUpToDate(member, existingMem)) {
						// we do not trust leader's alive status and state to come from outside
						if (currentLeaderInstanceId != null && existingMem != null &&
							member.InstanceId == currentLeaderInstanceId)
							members[member.HttpEndPoint] =
								member.Updated(utcNow: utcNow, isAlive: existingMem.IsAlive,
									state: existingMem.State);
						else
							members[member.HttpEndPoint] = member;
					}

					if (peerNode != null && isPeerOld) {
						MemberInfo newInfo = members[member.HttpEndPoint];
						// if we don't have past information about es version of the node, es version is unknown because old peer won't be sending version info in gossip
						members[member.HttpEndPoint] = newInfo.Updated(newInfo.TimeStamp,
							esVersion: existingMem?.ESVersion ?? VersionInfo.UnknownVersion);
					}
				}
			}

			var newMembers = members.Values.Select(update)
				.Where(x => KeepNodeInGossip(x, utcNow, deadMemberRemovalTimeout, me.State));
			return new ClusterInfo(newMembers);
		}

		private static bool IsMoreUpToDate(MemberInfo member, MemberInfo existingMem) {
			if (member.EpochNumber != existingMem.EpochNumber)
				return member.EpochNumber > existingMem.EpochNumber;
			if (member.WriterCheckpoint != existingMem.WriterCheckpoint)
				return member.WriterCheckpoint > existingMem.WriterCheckpoint;
			return member.TimeStamp > existingMem.TimeStamp;
		}

		public static ClusterInfo UpdateCluster(ClusterInfo cluster, Func<MemberInfo, MemberInfo> update,
			ITimeProvider timeProvider, TimeSpan deadMemberRemovalTimeout, VNodeState currentRole) {

			var newMembers = cluster.Members.Select(update)
				.Where(x => KeepNodeInGossip(x, timeProvider.UtcNow, deadMemberRemovalTimeout, currentRole));
			return new ClusterInfo(newMembers);
		}

		private static bool KeepNodeInGossip(MemberInfo m, DateTime utcNow, TimeSpan deadMemberRemovalTimeout, VNodeState currentRole) {
			// remove dead timed-out members, if there are any, and if we are not in an unknown/initializing/leaderless state
			return m.IsAlive || utcNow - m.TimeStamp < deadMemberRemovalTimeout
				|| currentRole <= VNodeState.Unknown || currentRole == VNodeState.ReadOnlyLeaderless;
		}

		private static void LogClusterChange(ClusterInfo oldCluster, ClusterInfo newCluster, string source) {
			var endPointComparer = new EndPointComparer();

			List<MemberInfo> oldMembers = oldCluster.Members
				.OrderByDescending(x => x.HttpEndPoint, endPointComparer).ToList();
			List<MemberInfo> newMembers = newCluster.Members
				.OrderByDescending(x => x.HttpEndPoint, endPointComparer).ToList();
			Log.Information(
				"CLUSTER HAS CHANGED {source}"
				+ "\nOld:"
				+ "\n{oldMembers}"
				+ "\nNew:"
				+ "\n{newMembers}"
				, source.IsNotEmptyString() ? source : string.Empty
				, oldMembers
				, newMembers
			);
		}
	}
}
