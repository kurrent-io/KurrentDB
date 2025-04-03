// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using EventStore.Core.Bus;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Gossip;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests.Services.TimeService;
using EventStore.Core.TransactionLog.Checkpoint;
using NUnit.Framework;
using SUT = EventStore.Core.Services.ElectionsService;

namespace EventStore.Core.Tests.Services.ElectionsService;

public class ElectionsServiceUnitTests {
	private Dictionary<IPEndPoint, IPublisher> _nodes;
	private List<MemberInfo> _members;
	private FakeTimeProvider _fakeTimeProvider;
	private FakeScheduler _scheduler;

	[SetUp]
	public void Setup() {
		var address = IPAddress.Loopback;
		var members = new List<MemberInfo>();
		var seeds = new List<IPEndPoint>();
		var seedSource = new ReallyNotSafeFakeGossipSeedSource(seeds);
		_nodes = new Dictionary<IPEndPoint, IPublisher>();
		for (int i = 0; i < 3; i++) {
			var inputBus = new SynchronousScheduler($"ELECTIONS-INPUT-BUS-NODE-{i}", watchSlowMsg: false);
			var outputBus = new SynchronousScheduler($"ELECTIONS-OUTPUT-BUS-NODE-{i}", watchSlowMsg: false);
			var endPoint = new IPEndPoint(address, 1000 + i);
			seeds.Add(endPoint);
			var instanceId = Guid.Parse($"101EFD13-F9CD-49BE-9C6D-E6AF9AF5540{i}");
			var memberInfo = MemberInfo.ForVNode(instanceId, DateTime.UtcNow, VNodeState.Unknown, true,
				endPoint, null, endPoint, null, endPoint, null, 0, 0, -1, 0, 0, -1, -1, Guid.Empty, 0, false);
			members.Add(memberInfo);
			_fakeTimeProvider = new FakeTimeProvider();
			_scheduler = new FakeScheduler(new FakeTimer(), _fakeTimeProvider);
			var timerService = new TimerService(_scheduler);

			var writerCheckpoint = new InMemoryCheckpoint();
			var readerCheckpoint = new InMemoryCheckpoint();
			var proposalCheckpoint = new InMemoryCheckpoint(-1);
			var epochManager = new FakeEpochManager();
			Func<long> lastCommitPosition = () => -1;
			var electionsService = new Core.Services.ElectionsService(outputBus,
				memberInfo,
				3,
				writerCheckpoint,
				readerCheckpoint,
				proposalCheckpoint,
				epochManager,
				() => -1, 0, new FakeTimeProvider(),
				TimeSpan.FromMilliseconds(1_000));
			electionsService.SubscribeMessages(inputBus);

			var nodeId = i;
			outputBus.Subscribe(new AdHocHandler<Message>(
				m => {
					switch (m) {
						case TimerMessage.Schedule sm:
							TestContext.WriteLine(
								$"Node {nodeId} : Delay {sm.TriggerAfter} : {sm.ReplyMessage.GetType()}");
							timerService.Handle(sm);
							break;
						default:
							TestContext.WriteLine($"Node {nodeId} : EP {m.GetType()}");
							inputBus.Publish(m);
							break;
					}
				}
			));
			_nodes.Add(endPoint, inputBus);

			var gossip = new NodeGossipService(outputBus, 3, seedSource, memberInfo, writerCheckpoint, readerCheckpoint,
				epochManager, lastCommitPosition, 0, TimeSpan.FromMilliseconds(500), TimeSpan.FromDays(1),
				TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1800), _fakeTimeProvider);
			inputBus.Subscribe<SystemMessage.SystemInit>(gossip);
			inputBus.Subscribe<GossipMessage.RetrieveGossipSeedSources>(gossip);
			inputBus.Subscribe<GossipMessage.GotGossipSeedSources>(gossip);
			inputBus.Subscribe<GossipMessage.Gossip>(gossip);
			inputBus.Subscribe<GossipMessage.GossipReceived>(gossip);
			inputBus.Subscribe<SystemMessage.StateChangeMessage>(gossip);
			inputBus.Subscribe<GossipMessage.GossipSendFailed>(gossip);
			inputBus.Subscribe<GossipMessage.UpdateNodePriority>(gossip);
			inputBus.Subscribe<SystemMessage.VNodeConnectionEstablished>(gossip);
			inputBus.Subscribe<SystemMessage.VNodeConnectionLost>(gossip);
		}

		_members = members;
	}

	class ReallyNotSafeFakeGossipSeedSource : IGossipSeedSource {
		private readonly List<IPEndPoint> _ipEndPoints;

		public ReallyNotSafeFakeGossipSeedSource(List<IPEndPoint> ipEndPoints) {
			_ipEndPoints = ipEndPoints;
		}

		public IAsyncResult BeginGetHostEndpoints(AsyncCallback requestCallback, object state) {
			requestCallback(null);
			return null;
		}

		public EndPoint[] EndGetHostEndpoints(IAsyncResult asyncResult) {
			return _ipEndPoints.ToArray();
		}
	}
}

public class ChoosingLeaderTests {
	static IEnumerable<TestCase> CreateCases() {
		// Tiebreaker: If all else is equal, pick the node with the highest server id
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 2,
			ProposingNode = 0,
		};

		// If checkpoints are equal, pick the node with the highest priority
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 0,
			NodePriorities = new[] { 3, 2, 1 }
		};

		// Don't pick the last elected leader if it is resigning
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 1,
			NodePriorities = new[] { 0, 0, 0 },
			LastElectedLeader = new int?[] { 2, 2, 2 },
			ResigningLeader = 2
		};

		// Pick the last elected leader if all else is equal
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 0,
			NodePriorities = new[] { int.MinValue, int.MinValue, int.MinValue },
			LastElectedLeader = new int?[] { 0, 0, 0 },
		};

		// Pick the non-resigning node with the highest chaser checkpoint
		// instead of the resigning leader
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 0,
			LastElectedLeader = new int?[] { 2, 2, 2 },
			ResigningLeader = 2,
			ChaserCheckpoints = new long[] { 1, 0, 1 },
			NodePriorities = new[] { 0, 0, 0 },
		};

		// Pick the non-resigning node with the highest writer checkpoint
		// instead of the resigning leader
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 1,
			LastElectedLeader = new int?[] { 2, 2, 2 },
			ResigningLeader = 2,
			WriterCheckpoints = new long[] { 0, 1, 1 },
			NodePriorities = new[] { 0, 0, 0 },
		};

		// Pick the node with the highest chaser checkpoint
		// regardless of node priority
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 0,
			LastElectedLeader = new int?[] { 0, 0, 0 },
			ChaserCheckpoints = new long[] { 1, 0, 0 },
			NodePriorities = new[] { int.MinValue, 0, 0 },
		};

		// Pick the node with the highest writer checkpoint
		// regardless of node priority
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 1,
			LastElectedLeader = new int?[] { 1, 1, 1 },
			WriterCheckpoints = new long[] { 0, 1, 0 },
			NodePriorities = new[] { 0, int.MinValue, 0 },
		};

		// Pick the node with the highest priority over the resigning leader
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 0,
			ResigningLeader = 1,
			LastElectedLeader = new int?[] { 1, 1, 1 },
			NodePriorities = new[] { 0, int.MinValue, int.MinValue },
		};

		// Pick the node with the highest priority between non-resigning nodes
		// over the resigning leader
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 2,
			ResigningLeader = 0,
			LastElectedLeader = new int?[] { 0, 0, 0 },
			ChaserCheckpoints = new long[] { 1, 1, 1 },
			NodePriorities = new[] { int.MaxValue, 0, 1 },
		};

		// Pick the node with the highest chaser checkpoint
		// over the resigning leader, despite node priority
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 2,
			ResigningLeader = 0,
			LastElectedLeader = new int?[] { 0, 0, 0 },
			ChaserCheckpoints = new long[] { 1, 0, 1 },
			NodePriorities = new[] { int.MinValue, 0, int.MinValue },
		};

		// Pick the node with the highest writer checkpoint
		// over the resigning leader, despite node priority
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 2,
			WriterCheckpoints = new long[] { 1, 0, 1 },
			NodePriorities = new[] { int.MinValue, 0, int.MinValue },
			LastElectedLeader = new int?[] { 0, 0, 0 },
			ResigningLeader = 0
		};

		// Prefer the majority last leader, highest epoch, chaser, and priority
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 2,
			LastElectedLeader = new int?[] { 0, 2, 2 },
			EpochNumbers = new[] { 10, 11, 11 },
			ChaserCheckpoints = new long[] { 0, 0, 5 },
			NodePriorities = new[] { 0, 0, 1 },
		};

		// Prefer the majority last leader, highest chaser, and priority
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 2,
			LastElectedLeader = new int?[] { 0, 2, 2 },
			EpochNumbers = new[] { 5, 5, 5 },
			ChaserCheckpoints = new long[] { 0, 0, 5 },
			NodePriorities = new[] { 0, 0, 1 },
		};

		// Prefer the majority last leader, highest epoch, chaser, writer, and priority
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 2,
			LastElectedLeader = new int?[] { 0, 2, 2 },
			EpochNumbers = new[] { 10, 11, 11 },
			WriterCheckpoints = new[] { 10L, 5, 5 },
			ChaserCheckpoints = new long[] { 0, 5, 5 },
			NodePriorities = new[] { 0, 0, 1 },
		};

		// Prefer the majority last leader, highest epoch, chaser, writer
		// despite priority
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 1,
			LastElectedLeader = new int?[] { 0, 1, 1 },
			EpochNumbers = new[] { 10, 11, 11 },
			WriterCheckpoints = new[] { 10L, 5, 5 },
			ChaserCheckpoints = new long[] { 0, 5, 5 },
			NodePriorities = new[] { 0, 0, 1 },
		};

		// Prefer the majority last leader, highest epoch, and chaser,
		// despite writer checkpoint
		yield return new TestCase {
			ExpectedLeaderCandidateNode = 2,
			LastElectedLeader = new int?[] { 0, 2, 2 },
			EpochNumbers = new[] { 10, 11, 11 },
			WriterCheckpoints = new[] { 10L, 5, 5 },
			ChaserCheckpoints = new long[] { 0, 5, 5 },
			NodePriorities = new[] { 0, 0, 0 },
		};
	}

	[Test, TestCaseSource(nameof(TestCases))]
	public void should_select_valid_best_leader_candidate(TestCase tc) {
		var epochId = Guid.NewGuid();
		var members = new MemberInfo[3];
		var prepareOks = new Dictionary<Guid, ElectionMessage.PrepareOk>();

		Func<int, long> writerCheckpoint = i => tc.WriterCheckpoints[i];
		Func<int, long> chaserCheckpoint = i => tc.ChaserCheckpoints[i];
		Func<int, int> nodePriority = i => tc.NodePriorities[i];
		Func<int, int> epochNumber = i => tc.EpochNumbers[i];

		for (int index = 0; index < 3; index++) {
			members[index] = CreateMemberInfo(index, epochId, writerCheckpoint,
				chaserCheckpoint, nodePriority, epochNumber);
		}

		var clusterInfo = new ClusterInfo(members);
		Func<int, Guid> previousLeaderId = i => !tc.LastElectedLeader[i].HasValue ? Guid.Empty : IdForNode(tc.LastElectedLeader[i].Value);

		for (int index = 0; index < 3; index++) {
			var prepareOk = CreatePrepareOk(index, epochId, writerCheckpoint, chaserCheckpoint,
				nodePriority, epochNumber, previousLeaderId, clusterInfo);
			prepareOks.Add(prepareOk.ServerId, prepareOk);
		}

		var resigningLeadership = tc.ResigningLeader.HasValue
			? (Guid?)IdForNode(tc.ResigningLeader.Value)
			: null;
		var mc = SUT.GetBestLeaderCandidate(prepareOks, members, resigningLeadership, 0);

		Assert.AreEqual(IdForNode(tc.ExpectedLeaderCandidateNode), mc.InstanceId, $"Expected node {tc.ExpectedLeaderCandidateNode} got node {Array.FindIndex(members, 0, m => m.InstanceId == mc.InstanceId) + 1 }");

		var ownInfo = CreateLeaderCandidate(1, epochId, writerCheckpoint, chaserCheckpoint,
			nodePriority, epochNumber, previousLeaderId);

		var isLegit = SUT.IsLegitimateLeader(1, EndpointForNode(tc.ProposingNode),
			IdForNode(tc.ProposingNode), mc, members, null, members[0].InstanceId,
			ownInfo, resigningLeadership);

		Assert.True(isLegit);
	}


	static ElectionMessage.PrepareOk CreatePrepareOk(int i, Guid epochId,
		Func<int, long> writerCheckpoint,
		Func<int, long> chaserCheckpoint,
		Func<int, int> nodePriority,
		Func<int, int> epochNumber,
		Func<int, Guid> previousLeaderId,
		ClusterInfo clusterInfo) {
		var id = IdForNode(i);
		var ep = EndpointForNode(i);
		return new ElectionMessage.PrepareOk(1, id, ep, epochNumber(i), 1, epochId, previousLeaderId(i), -1, writerCheckpoint(i),
			chaserCheckpoint(i), nodePriority(i), clusterInfo);
	}

	static SUT.LeaderCandidate CreateLeaderCandidate(int i, Guid epochId,
		Func<int, long> writerCheckpoint,
		Func<int, long> chaserCheckpoint,
		Func<int, int> nodePriority,
		Func<int, int> epochNumber,
		Func<int, Guid> previousLeaderId) {
		var id = IdForNode(i);
		var ep = EndpointForNode(i);
		return new SUT.LeaderCandidate(id, ep, epochNumber(i), 1, epochId, previousLeaderId(i), -1, writerCheckpoint(i),
			chaserCheckpoint(i), nodePriority(i));
	}

	static MemberInfo CreateMemberInfo(int i, Guid epochId,
		Func<int, long> writerCheckpoint,
		Func<int, long> chaserCheckpoint,
		Func<int, int> nodePriority,
		Func<int, int> epochNumber
		) {
		var id = IdForNode(i);
		var ep = EndpointForNode(i);
		return MemberInfo.ForVNode(id, DateTime.Now, VNodeState.Follower, true, ep, ep, ep, ep, ep, null, 0, 0,
			 -1, writerCheckpoint(i), chaserCheckpoint(i), 1, epochNumber(i), epochId, nodePriority(i), false);
	}

	private static IPEndPoint EndpointForNode(int i) {
		return new IPEndPoint(IPAddress.Loopback, 1000 + i);
	}

	private static Guid IdForNode(int i) {
		return Guid.Parse($"101EFD13-F9CD-49BE-9C6D-E6AF9AF5540{i}");
	}

	static object[] TestCases() {
		return CreateCases().Cast<object>().ToArray();
	}

	public class TestCase {
		public int ExpectedLeaderCandidateNode { get; set; }
		public int? ResigningLeader { get; set; }
		public int ProposingNode { get; set; }
		public int?[] LastElectedLeader { get; set; } = { null, null, null };
		public long[] WriterCheckpoints { get; set; } = { 1L, 1, 1 };
		public long[] ChaserCheckpoints { get; set; } = { 1L, 1, 1 };
		public int[] NodePriorities { get; set; } = { 1, 1, 1 };
		public int[] EpochNumbers { get; set; } = { -1, -1, -1 };

		private static int _id = 0;
		private static string GenerateName(int expectedLeaderCandidateNode, int?[] previousLeader,
			long[] writerCheckpoints,
			long[] chaserCheckpoints, int[] nodePriorities, int[] epochNumbers) {
			var nameBuilder = new StringBuilder();
			nameBuilder.Append($"{_id++} ");
			if (writerCheckpoints != null) {
				if (nameBuilder.Length == 0)
					nameBuilder.Append("Nodes with ");
				else
					nameBuilder.Append(" and ");
				nameBuilder.AppendFormat("writer checkpoints << {0} >>", string.Join(",",
					writerCheckpoints.Where(x => x != 1).Select((x, i) => $"{i} : wcp {x}")));
			}

			if (chaserCheckpoints != null) {
				if (nameBuilder.Length == 0)
					nameBuilder.Append("Nodes with ");
				else
					nameBuilder.Append(" and ");
				nameBuilder.AppendFormat("chaser checkpoints << {0} >>", string.Join(",",
					chaserCheckpoints.Where(x => x != 1).Select((x, i) => $"{i} : ccp {x}")));
			}

			if (nodePriorities != null) {
				if (nameBuilder.Length == 0)
					nameBuilder.Append("Nodes with ");
				else
					nameBuilder.Append(" and ");
				nameBuilder.AppendFormat("node priorities << {0} >>", string.Join(",",
					nodePriorities.Where(x => x != 0).Select((x, i) => $"{i} : np {x}")));
			}
			if (epochNumbers != null) {
				nameBuilder.Append(nameBuilder.Length == 0 ? "Nodes with " : " and ");
				nameBuilder.AppendFormat("epoch numbers << {0} >>", string.Join(",",
					epochNumbers.Where(x => x != 0).Select((x, i) => $"{i} : en {x}")));
			}

			if (nameBuilder.Length == 0)
				nameBuilder.AppendFormat("All nodes caught up with the same priority expect {0} to be leader",
					expectedLeaderCandidateNode);
			var name = nameBuilder.ToString();
			return name;
		}

		public override string ToString() {
			return GenerateName(ExpectedLeaderCandidateNode, LastElectedLeader, WriterCheckpoints, ChaserCheckpoints,
				NodePriorities, EpochNumbers);
		}
	}
}
