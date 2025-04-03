// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Gossip;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Services.ElectionsService;
using EventStore.Core.Tests.Services.TimeService;
using EventStore.Core.TransactionLog.Checkpoint;
using FluentAssertions;
using NUnit.Framework;
using MemberInfo = EventStore.Core.Cluster.MemberInfo;

namespace EventStore.Core.Tests.Services.GossipService;

public abstract class NodeGossipServiceTestFixture {
	protected NodeGossipService SUT;
	protected FakePublisher _bus;
	protected VNodeInfo _currentNode;
	protected VNodeInfo _nodeTwo;
	protected VNodeInfo _nodeThree;
	protected VNodeInfo _nodeFour;
	protected ITimeProvider _timeProvider;
	protected Func<MemberInfo[], MemberInfo> _getNodeToGossipTo;
	protected IGossipSeedSource _gossipSeedSource;
	protected TimeSpan _gossipInterval = TimeSpan.FromMilliseconds(1000);
	private readonly TimeSpan _allowedTimeDifference = TimeSpan.FromMilliseconds(1000);
	protected readonly TimeSpan _gossipTimeout = TimeSpan.FromMilliseconds(1000);
	private readonly TimeSpan _deadMemberRemovalPeriod = TimeSpan.FromSeconds(1800);


	public NodeGossipServiceTestFixture() {
		_bus = new FakePublisher();
		_timeProvider = new FakeTimeProvider();

		_currentNode = new VNodeInfo(
			Guid.Parse("00000000-0000-0000-0000-000000000001"), 1,
			new IPEndPoint(IPAddress.Loopback, 1111),
			new IPEndPoint(IPAddress.Loopback, 1111),
			new IPEndPoint(IPAddress.Loopback, 1111),
			new IPEndPoint(IPAddress.Loopback, 1111),
			new IPEndPoint(IPAddress.Loopback, 1111), false);
		_nodeTwo = new VNodeInfo(
			Guid.Parse("00000000-0000-0000-0000-000000000002"), 2,
			new IPEndPoint(IPAddress.Loopback, 2222),
			new IPEndPoint(IPAddress.Loopback, 2222),
			new IPEndPoint(IPAddress.Loopback, 2222),
			new IPEndPoint(IPAddress.Loopback, 2222),
			new IPEndPoint(IPAddress.Loopback, 2222), false);
		_nodeThree = new VNodeInfo(
			Guid.Parse("00000000-0000-0000-0000-000000000003"), 3,
			new IPEndPoint(IPAddress.Loopback, 3333),
			new IPEndPoint(IPAddress.Loopback, 3333),
			new IPEndPoint(IPAddress.Loopback, 3333),
			new IPEndPoint(IPAddress.Loopback, 3333),
			new IPEndPoint(IPAddress.Loopback, 3333), false);
		_nodeFour = new VNodeInfo(
			Guid.Parse("00000000-0000-0000-0000-000000000004"), 4,
			new IPEndPoint(IPAddress.Loopback, 4444),
			new IPEndPoint(IPAddress.Loopback, 4444),
			new IPEndPoint(IPAddress.Loopback, 4444),
			new IPEndPoint(IPAddress.Loopback, 4444),
			new IPEndPoint(IPAddress.Loopback, 4444), false);

		_getNodeToGossipTo = infos => infos.First(x => Equals(x.HttpEndPoint, _nodeTwo.HttpEndPoint));
		_gossipSeedSource = new KnownEndpointGossipSeedSource(new[]
			{_currentNode.HttpEndPoint, _nodeTwo.HttpEndPoint, _nodeThree.HttpEndPoint});
	}

	[SetUp]
	public void Setup() {
		SUT = new NodeGossipService(_bus, 3, _gossipSeedSource, MemberInfoForVNode(_currentNode, DateTime.UtcNow),
			new InMemoryCheckpoint(0), new InMemoryCheckpoint(0), new FakeEpochManager(), () => 0L, 0,
			_gossipInterval, _allowedTimeDifference, _gossipTimeout, _deadMemberRemovalPeriod, _timeProvider, _getNodeToGossipTo);

		foreach (var message in Given()) {
			SUT.Handle((dynamic)message);
		}

		_bus.Messages.Clear();

		var when = When();
		if (when != null) {
			SUT.Handle((dynamic)when);
		}
	}

	protected virtual Message[] Given() => Array.Empty<Message>();

	protected virtual Message When() => null;

	protected void ExpectMessages(params Message[] expected) =>
		_bus.Messages.Should().BeEquivalentTo(expected);

	protected void ExpectNoMessages() =>
		Assert.IsEmpty(_bus.Messages);

	protected Message[] GivenSystemInitializedWithKnownGossipSeedSources(params Message[] additionalGivens) {
		return new Message[] {
			new SystemMessage.SystemInit(),
			new GossipMessage.GotGossipSeedSources(new[]
				{_currentNode.HttpEndPoint, _nodeTwo.HttpEndPoint, _nodeThree.HttpEndPoint})
		}.Concat(additionalGivens).ToArray();
	}

	protected static MemberInfo MemberInfoForVNode(VNodeInfo nodeInfo, DateTime utcNow,
		int? nodePriority = null, int? epochNumber = null, long? writerCheckpoint = null,
		VNodeState nodeState = VNodeState.Initializing, string esVersion = VersionInfo.DefaultVersion, bool isAlive = true) {
		return MemberInfo.ForVNode(nodeInfo.InstanceId, utcNow, nodeState, isAlive,
			nodeInfo.InternalTcp, nodeInfo.InternalSecureTcp, nodeInfo.ExternalTcp,
			nodeInfo.ExternalSecureTcp, nodeInfo.HttpEndPoint, null, 0, 0,
			0, writerCheckpoint ?? 0, 0, -1, epochNumber ?? -1, Guid.Empty, nodePriority ?? 0, false, esVersion);
	}

	/// <summary>
	/// The initial state for a node currently is represented as a Manager
	/// </summary>
	protected static MemberInfo InitialStateForVNode(VNodeInfo nodeInfo, DateTime utcNow, bool isAlive = true, string version = VersionInfo.UnknownVersion) {
		return MemberInfo.ForManager(Guid.Empty, utcNow, isAlive, nodeInfo.HttpEndPoint, esVersion: version);
	}
}

public class when_system_initializes : NodeGossipServiceTestFixture {
	protected override Message[] Given() =>
		Array.Empty<Message>();

	protected override Message When() =>
		new SystemMessage.SystemInit();

	[Test]
	public void should_get_gossip_sources() {
		ExpectMessages(
			new GossipMessage.GotGossipSeedSources(new[]
				{_currentNode.HttpEndPoint, _nodeTwo.HttpEndPoint, _nodeThree.HttpEndPoint}));
	}
}

public class when_system_initializes_twice : NodeGossipServiceTestFixture {
	protected override Message[] Given() => new Message[] {
		new SystemMessage.SystemInit()
	};

	protected override Message When() =>
		new SystemMessage.SystemInit();

	[Test]
	public void should_ignore_system_init() {
		ExpectNoMessages();
	}
}

public class when_retrieving_gossip_seed_sources : NodeGossipServiceTestFixture {
	protected override Message[] Given() => new Message[] {
		new SystemMessage.SystemInit()
	};

	protected override Message When() =>
		new GossipMessage.RetrieveGossipSeedSources();

	[Test]
	public void should_get_gossip_seeds() {
		ExpectMessages(
			new GossipMessage.GotGossipSeedSources(new[]
				{_currentNode.HttpEndPoint, _nodeTwo.HttpEndPoint, _nodeThree.HttpEndPoint}));
	}
}

public class when_retrieving_gossip_seed_sources_and_gossip_seed_source_throws : NodeGossipServiceTestFixture {
	class ThrowingGossipSeedSource : IGossipSeedSource {
		public IAsyncResult BeginGetHostEndpoints(AsyncCallback requestCallback, object state) {
			throw new NotImplementedException();
		}

		public EndPoint[] EndGetHostEndpoints(IAsyncResult asyncResult) {
			throw new NotImplementedException();
		}
	}

	public when_retrieving_gossip_seed_sources_and_gossip_seed_source_throws() {
		_gossipSeedSource = new ThrowingGossipSeedSource();
	}

	protected override Message[] Given() => new Message[] {
		new SystemMessage.SystemInit()
	};

	protected override Message When() =>
		new GossipMessage.RetrieveGossipSeedSources();

	[Test]
	public void should_schedule_retry_retrieve_gossip_seed_sources() {
		ExpectMessages(
			TimerMessage.Schedule.Create(GossipServiceBase.DnsRetryTimeout, _bus,
				new GossipMessage.RetrieveGossipSeedSources()));
	}
}

public class when_got_gossip_seed_sources : NodeGossipServiceTestFixture {
	protected override Message[] Given() => new Message[] {
		new SystemMessage.SystemInit()
	};

	protected override Message When() =>
		new GossipMessage.GotGossipSeedSources(new[]
			{_currentNode.HttpEndPoint, _nodeTwo.HttpEndPoint, _nodeThree.HttpEndPoint});

	[Test]
	public void should_start_gossiping_and_schedule_another_gossip() {
		ExpectMessages(
			new GrpcMessage.SendOverGrpc(_nodeTwo.HttpEndPoint, new GossipMessage.SendGossip(new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow),
					InitialStateForVNode(_nodeThree, _timeProvider.UtcNow)),
				_currentNode.HttpEndPoint), _timeProvider.LocalTime.Add(_gossipTimeout)),
			TimerMessage.Schedule.Create(GossipServiceBase.GossipStartupInterval, _bus,
				new GossipMessage.Gossip(1)));
	}
}

public class when_gossip : NodeGossipServiceTestFixture {
	private int _gossipRound = GossipServiceBase.GossipRoundStartupThreshold + 1;

	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources();

	protected override Message When() =>
		new GossipMessage.Gossip(_gossipRound);

	[Test]
	public void should_send_the_gossip_over_http_and_schedule_the_next_gossip() {
		ExpectMessages(
			new GrpcMessage.SendOverGrpc(_nodeTwo.HttpEndPoint, new GossipMessage.SendGossip(new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow),
					InitialStateForVNode(_nodeThree, _timeProvider.UtcNow)),
				_currentNode.HttpEndPoint), _timeProvider.LocalTime.Add(_gossipTimeout)),
			TimerMessage.Schedule.Create(_gossipInterval, _bus,
				new GossipMessage.Gossip(++_gossipRound)));
	}
}

public class when_gossip_and_no_node_is_selected_to_gossip_to : NodeGossipServiceTestFixture {
	private int _gossipRound = GossipServiceBase.GossipRoundStartupThreshold + 1;

	public when_gossip_and_no_node_is_selected_to_gossip_to() {
		_getNodeToGossipTo = infos => null;
	}

	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources();

	protected override Message When() =>
		new GossipMessage.Gossip(_gossipRound);

	[Test]
	public void should_just_schedule_next_gossip() {
		ExpectMessages(TimerMessage.Schedule.Create(_gossipInterval, _bus,
			new GossipMessage.Gossip(_gossipRound)));
	}
}

public class when_gossip_and_gossip_service_is_not_in_working_state : NodeGossipServiceTestFixture {
	protected override Message[] Given() => Array.Empty<Message>();

	protected override Message When() =>
		new GossipMessage.Gossip(1);

	[Test]
	public void should_ignore_message() {
		ExpectNoMessages();
	}
}

public class when_gossip_and_gossip_round_less_than_startup_gossip_threshold : NodeGossipServiceTestFixture {
	private int _gossipRound = new Random().Next(0, GossipServiceBase.GossipRoundStartupThreshold);

	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources();

	protected override Message When() =>
		new GossipMessage.Gossip(_gossipRound);

	[Test]
	public void should_use_startup_gossip_interval() {
		ExpectMessages(
			new GrpcMessage.SendOverGrpc(_nodeTwo.HttpEndPoint, new GossipMessage.SendGossip(new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow),
					InitialStateForVNode(_nodeThree, _timeProvider.UtcNow)),
				_currentNode.HttpEndPoint), _timeProvider.LocalTime.Add(_gossipTimeout)),
			TimerMessage.Schedule.Create(GossipServiceBase.GossipStartupInterval, _bus,
				new GossipMessage.Gossip(++_gossipRound)));
	}
}

public class when_gossip_and_gossip_round_larger_than_startup_gossip_threshold : NodeGossipServiceTestFixture {
	private int _gossipRound = new Random().Next(GossipServiceBase.GossipRoundStartupThreshold, int.MaxValue);

	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources();

	protected override Message When() =>
		new GossipMessage.Gossip(_gossipRound);

	[Test]
	public void should_use_provided_gossip_interval_for_next_gossip() {
		ExpectMessages(
			new GrpcMessage.SendOverGrpc(_nodeTwo.HttpEndPoint, new GossipMessage.SendGossip(new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow),
					InitialStateForVNode(_nodeThree, _timeProvider.UtcNow)),
				_currentNode.HttpEndPoint), _timeProvider.LocalTime.Add(_gossipTimeout)),
			TimerMessage.Schedule.Create(_gossipInterval, _bus,
				new GossipMessage.Gossip(++_gossipRound)));
	}
}

public class when_gossip_received_with_older_timestamp_about_peer_node :
	NodeGossipServiceTestFixture {
	private readonly DateTime _timestamp = DateTime.Now;

	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeTwo, _timestamp),
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow)),
				_nodeTwo.HttpEndPoint)
		);

	protected override Message When() =>
		new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timestamp.AddMilliseconds(-1)),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow)),
			_nodeTwo.HttpEndPoint);

	[Test]
	public void should_accept_the_information_about_the_node_even_if_outdated() {
		ExpectMessages(
			new GossipMessage.GossipUpdated(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timestamp.AddMilliseconds(-1)),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow))));
	}
}

public class if_gossip_reply_includes_es_version : NodeGossipServiceTestFixture {
	private Message _capturedMessage;
	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources();

	protected override Message When() =>
		new GossipMessage.GossipReceived(new CallbackEnvelope(CaptureGossipReply), new ClusterInfo(
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber:  1, esVersion: "1.1.1.2"),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3")),
			_nodeTwo.HttpEndPoint);

	private ClusterInfo GetExpectedClusterInfo() {
		return new ClusterInfo(MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, esVersion: VersionInfo.DefaultVersion),
			MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.2"),
			MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3"));
	}

	[Test]
	public void gossip_update_must_have_es_version() {
		//updated cluster info should have version info of currentNode, nodeTwo and nodeThree
		ExpectMessages(new GossipMessage.GossipUpdated(GetExpectedClusterInfo()));
		//gossip reply should have version info of currentNode, nodeTwo and nodeThree
		_capturedMessage.Should().BeEquivalentTo(new GossipMessage.SendGossip(GetExpectedClusterInfo(), _currentNode.HttpEndPoint));
	}

	private void CaptureGossipReply(Message message) => _capturedMessage = message;
}

public class if_gossip_read_reply_includes_es_version : NodeGossipServiceTestFixture {
	private Message _capturedMessage;

	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(new GossipMessage.GossipReceived(
			new CallbackEnvelope(CaptureGossipReply), new ClusterInfo(
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.2"),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3")),
			_nodeTwo.HttpEndPoint));

	protected override Message When() =>
		new GossipMessage.ReadGossip(new CallbackEnvelope(CaptureGossipReply));

	private ClusterInfo GetExpectedClusterInfo() {
		return new ClusterInfo(
			MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, esVersion: VersionInfo.DefaultVersion),
			MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.2"),
			MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3"));
	}

	[Test]
	public void reply_should_have_version_info() {
		_capturedMessage.Should()
			.BeEquivalentTo(new GossipMessage.SendGossip(GetExpectedClusterInfo(), _currentNode.HttpEndPoint));
	}

	private void CaptureGossipReply(Message message) => _capturedMessage = message;
}

public class if_client_gossip_reply_includes_es_version : NodeGossipServiceTestFixture {
	private Message _capturedMessage;
	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber:  1, esVersion: "1.1.1.2"),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3")),
			_nodeTwo.HttpEndPoint));

	protected override Message When() =>
		new GossipMessage.ClientGossip(new CallbackEnvelope(CaptureGossipReply));

	private ClientClusterInfo GetExpectedClusterInfo() {
		return new ClientClusterInfo(new ClusterInfo(
			MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, esVersion: VersionInfo.DefaultVersion),
			MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.2"),
			MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3")), _currentNode.HttpEndPoint.GetHost(), _currentNode.HttpEndPoint.GetPort());
	}

	[Test]
	public void reply_should_have_version_info() {
		_capturedMessage.Should()
			.BeEquivalentTo(new GossipMessage.SendClientGossip(GetExpectedClusterInfo()));
	}

	private void CaptureGossipReply(Message message) => _capturedMessage = message;
}

public class when_gossip_received_with_lower_epoch_number_about_peer_node :
	NodeGossipServiceTestFixture {
	private readonly int _epochNumber = new Random().Next();

	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: _epochNumber),
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow)),
				_nodeTwo.HttpEndPoint)
		);

	protected override Message When() =>
		new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: _epochNumber - 1),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow)),
			_nodeTwo.HttpEndPoint);

	[Test]
	public void should_accept_the_information_about_the_node_even_if_outdated() {
		ExpectMessages(
			new GossipMessage.GossipUpdated(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: _epochNumber - 1),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow))));
	}
}

public class when_gossip_received_with_lower_writer_checkpoint_about_peer_node :
	NodeGossipServiceTestFixture {
	private readonly int _writerCheckpoint = new Random().Next();

	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, writerCheckpoint: _writerCheckpoint),
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow)),
				_nodeTwo.HttpEndPoint)
		);

	protected override Message When() =>
		new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, writerCheckpoint: _writerCheckpoint - 1),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow)),
			_nodeTwo.HttpEndPoint);

	[Test]
	public void should_accept_the_information_about_the_node_even_if_outdated() {
		ExpectMessages(
			new GossipMessage.GossipUpdated(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, writerCheckpoint: _writerCheckpoint - 1),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow))));
	}
}

public class when_gossip_received_with_more_recent_timestamp_about_non_peer_node :
	NodeGossipServiceTestFixture {
	private readonly DateTime _timestamp = DateTime.Now;

	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeThree, _timestamp)),
				_nodeTwo.HttpEndPoint)
		);

	protected override Message When() =>
		new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeThree, _timestamp.AddMilliseconds(1))),
			_nodeThree.HttpEndPoint);

	[Test]
	public void should_accept_the_information_if_its_more_recent() {
		ExpectMessages(
			new GossipMessage.GossipUpdated(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeThree, _timestamp.AddMilliseconds(1)))));
	}
}

public class when_gossip_received_with_higher_epoch_number_about_non_peer_node :
	NodeGossipServiceTestFixture {
	private readonly int _epochNumber = new Random().Next();

	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: _epochNumber)),
				_nodeTwo.HttpEndPoint)
		);

	protected override Message When() =>
		new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: _epochNumber + 1)),
			_nodeTwo.HttpEndPoint);

	[Test]
	public void should_accept_the_information_if_its_more_recent() {
		ExpectMessages(
			new GossipMessage.GossipUpdated(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: _epochNumber + 1))));
	}
}

public class when_gossip_received_with_higher_writer_checkpoint_about_non_peer_node :
	NodeGossipServiceTestFixture {
	private readonly int _writerCheckpoint = new Random().Next();

	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, writerCheckpoint: _writerCheckpoint)),
				_nodeTwo.HttpEndPoint)
		);

	protected override Message When() =>
		new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, writerCheckpoint: _writerCheckpoint + 1)),
			_nodeTwo.HttpEndPoint);

	[Test]
	public void should_accept_the_information_if_its_more_recent() {
		ExpectMessages(
			new GossipMessage.GossipUpdated(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, writerCheckpoint: _writerCheckpoint + 1))));
	}
}

public class when_state_changed_to_leader : NodeGossipServiceTestFixture {
	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, nodeState: VNodeState.Initializing),
					InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow),
					InitialStateForVNode(_nodeThree, _timeProvider.UtcNow)),
				_nodeTwo.HttpEndPoint)
		);

	protected override Message When() =>
		new SystemMessage.BecomeLeader(Guid.NewGuid());

	[Test]
	public void should_update_gossip() {
		ExpectMessages(
			new GossipMessage.GossipUpdated(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, nodeState: VNodeState.Leader),
				InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow),
				InitialStateForVNode(_nodeThree, _timeProvider.UtcNow))));
	}
}

public class when_state_changed_to_non_leader : NodeGossipServiceTestFixture {
	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, nodeState: VNodeState.Initializing),
				InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow),
				InitialStateForVNode(_nodeThree, _timeProvider.UtcNow)), _nodeTwo.HttpEndPoint)
		);

	protected override Message When() =>
		new SystemMessage.BecomeFollower(Guid.NewGuid(), MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow));

	[Test]
	public void should_update_gossip() {
		ExpectMessages(
			new GossipMessage.GossipUpdated(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, nodeState: VNodeState.Follower),
				InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow),
				InitialStateForVNode(_nodeThree, _timeProvider.UtcNow))));
	}
}

public class when_gossip_send_failed : NodeGossipServiceTestFixture {
	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources();

	protected override Message When() =>
		new GossipMessage.GossipSendFailed("failed", _nodeTwo.HttpEndPoint);

	[Test]
	public void should_mark_the_node_as_dead() {
		ExpectMessages(
			new GossipMessage.GossipUpdated(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow, isAlive: false),
				InitialStateForVNode(_nodeThree, _timeProvider.UtcNow))));
	}
}

public class when_gossip_send_failed_to_a_dead_node : NodeGossipServiceTestFixture {
	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow, isAlive: false),
				InitialStateForVNode(_nodeThree, _timeProvider.UtcNow)), _nodeTwo.HttpEndPoint)
		);

	protected override Message When() => new GossipMessage.GossipSendFailed("failed",
		_nodeTwo.HttpEndPoint);

	[Test]
	public void should_ignore_message() {
		ExpectNoMessages();
	}
}

public class when_gossip_send_failed_to_the_current_leader_node : NodeGossipServiceTestFixture {
	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, nodeState: VNodeState.Leader)),
				_nodeTwo.HttpEndPoint),
			new SystemMessage.BecomeFollower(Guid.NewGuid(), MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow))
		);

	protected override Message When() => new GossipMessage.GossipSendFailed("failed",
		_nodeTwo.HttpEndPoint);

	[Test]
	public void should_ignore_message_and_wait_for_tcp_to_decide() {
		ExpectNoMessages();
	}
}

public class when_vnode_connection_lost : NodeGossipServiceTestFixture {
	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow),
				InitialStateForVNode(_nodeThree, _timeProvider.UtcNow)), _nodeTwo.HttpEndPoint)
		);

	protected override Message When() =>
		new SystemMessage.VNodeConnectionLost(_currentNode.HttpEndPoint, Guid.NewGuid());

	[Test]
	public void should_issue_get_gossip() {
		ExpectMessages(
			new GrpcMessage.SendOverGrpc(_currentNode.HttpEndPoint, new GossipMessage.GetGossip(),
				_timeProvider.LocalTime.Add(_gossipTimeout)));
	}
}

public class when_vnode_connection_lost_to_dead_node : NodeGossipServiceTestFixture {
	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow, isAlive: false),
				InitialStateForVNode(_nodeThree, _timeProvider.UtcNow)), _nodeTwo.HttpEndPoint)
		);

	protected override Message When() =>
		new SystemMessage.VNodeConnectionLost(_nodeTwo.HttpEndPoint, Guid.NewGuid());

	[Test]
	public void should_ignore_message() {
		ExpectNoMessages();
	}
}

public class when_get_gossip_received_with_more_recent_timestamp_about_non_peer_node :
	NodeGossipServiceTestFixture {
	private readonly DateTime _timestamp = DateTime.Now;

	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeThree, _timestamp)),
				_nodeTwo.HttpEndPoint)
		);

	protected override Message When() =>
		new GossipMessage.GetGossipReceived(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeThree, _timestamp.AddMilliseconds(1))),
			_nodeThree.HttpEndPoint);

	[Test]
	public void should_accept_the_information_if_its_more_recent() {
		ExpectMessages(
			new GossipMessage.GossipUpdated(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeThree, _timestamp.AddMilliseconds(1)))));
	}
}

public class when_get_gossip_received_with_higher_epoch_number_about_non_peer_node :
	NodeGossipServiceTestFixture {
	private readonly int _epochNumber = new Random().Next();

	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: _epochNumber)),
				_nodeTwo.HttpEndPoint)
		);

	protected override Message When() =>
		new GossipMessage.GetGossipReceived(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: _epochNumber + 1)),
			_nodeTwo.HttpEndPoint);

	[Test]
	public void should_accept_the_information_if_its_more_recent() {
		ExpectMessages(
			new GossipMessage.GossipUpdated(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: _epochNumber + 1))));
	}
}

public class when_get_gossip_received_with_higher_writer_checkpoint_about_non_peer_node :
	NodeGossipServiceTestFixture {
	private readonly int _writerCheckpoint = new Random().Next();

	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow,
						writerCheckpoint: _writerCheckpoint)),
				_nodeTwo.HttpEndPoint)
		);

	protected override Message When() =>
		new GossipMessage.GetGossipReceived(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow,
					writerCheckpoint: _writerCheckpoint + 1)),
			_nodeTwo.HttpEndPoint);

	[Test]
	public void should_accept_the_information_if_its_more_recent() {
		ExpectMessages(
			new GossipMessage.GossipUpdated(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow,
					writerCheckpoint: _writerCheckpoint + 1))));
	}
}

public class
	when_get_gossip_received_and_gossip_service_is_not_in_working_state : NodeGossipServiceTestFixture {
	protected override Message[] Given() => Array.Empty<Message>();

	protected override Message When() =>
		new GossipMessage.GetGossipReceived(new ClusterInfo(), _nodeTwo.HttpEndPoint);

	[Test]
	public void should_ignore_message() {
		ExpectNoMessages();
	}
}

public class
	when_get_gossip_failed_and_gossip_service_is_not_in_working_state : NodeGossipServiceTestFixture {
	protected override Message[] Given() => Array.Empty<Message>();

	protected override Message When() =>
		new GossipMessage.GetGossipFailed("failed", _nodeTwo.HttpEndPoint);

	[Test]
	public void should_ignore_message() {
		ExpectNoMessages();
	}
}

public class when_get_gossip_failed : NodeGossipServiceTestFixture {
	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow, isAlive: true),
					InitialStateForVNode(_nodeThree, _timeProvider.UtcNow)),
				_currentNode.HttpEndPoint)
		);

	protected override Message When() =>
		new GossipMessage.GetGossipFailed("failed", _nodeTwo.HttpEndPoint);

	[Test]
	public void should_mark_node_as_dead() {
		ExpectMessages(
			new GossipMessage.GossipUpdated(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow, isAlive: false),
				InitialStateForVNode(_nodeThree, _timeProvider.UtcNow))));
	}
}

public class when_vnode_connection_established : NodeGossipServiceTestFixture {
	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
					InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow, isAlive: false),
					InitialStateForVNode(_nodeThree, _timeProvider.UtcNow)),
				_currentNode.HttpEndPoint)
		);

	protected override Message When() =>
		new SystemMessage.VNodeConnectionEstablished(_nodeTwo.HttpEndPoint, Guid.NewGuid());

	[Test]
	public void should_mark_node_as_alive() {
		ExpectMessages(
			new GossipMessage.GossipUpdated(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
				InitialStateForVNode(_nodeTwo, _timeProvider.UtcNow, isAlive: true),
				InitialStateForVNode(_nodeThree, _timeProvider.UtcNow))
			));
	}
}

public class when_elections_are_done : NodeGossipServiceTestFixture {
	protected override Message[] Given() =>
		GivenSystemInitializedWithKnownGossipSeedSources(
			new GossipMessage.GossipReceived(new NoopEnvelope(), new ClusterInfo(
					MemberInfoForVNode(_currentNode, _timeProvider.UtcNow,
						nodeState: VNodeState.Initializing),
					MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, nodeState: VNodeState.Initializing),
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow,
						nodeState: VNodeState.Initializing)),
				_nodeTwo.HttpEndPoint)
		);

	protected override Message When() =>
		new ElectionMessage.ElectionsDone(0,0,
			MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, nodeState: VNodeState.Leader));

	[Test]
	public void should_set_leader_node_and_other_nodes_to_unknown() {
		ExpectMessages(
			new GossipMessage.GossipUpdated(new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, nodeState: VNodeState.Unknown),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, nodeState: VNodeState.Leader),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, nodeState: VNodeState.Unknown))));
	}
}

public class when_updating_node_priority : NodeGossipServiceTestFixture {
	private readonly int _nodePriority = new Random().Next();
	protected override Message[] Given() => GivenSystemInitializedWithKnownGossipSeedSources();

	protected override Message When() {
		return new GossipMessage.UpdateNodePriority(_nodePriority);
	}

	[Test]
	public void should_set_node_priority() {
		var clusterInfo = new ClusterInfo(
			MemberInfoForVNode(_currentNode, _timeProvider.UtcNow),
			MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow),
			MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow));

		SUT.Handle(new GossipMessage.GossipReceived(new NoopEnvelope(), clusterInfo,
			_nodeTwo.HttpEndPoint));
		var memberInfo = _bus.Messages.OfType<GossipMessage.GossipUpdated>().First().ClusterInfo.Members
			.First(x => x.InstanceId == _currentNode.InstanceId);
		Assert.AreEqual(memberInfo.NodePriority, _nodePriority);
	}
}

public class when_updating_cluster {
	private static MemberInfo TestNodeFor(int identifier, bool isAlive, DateTime timeStamp) {
		var ipEndpoint = new IPEndPoint(IPAddress.Loopback, identifier);
		return MemberInfo.ForVNode(Guid.NewGuid(), timeStamp, VNodeState.Initializing, isAlive,
			ipEndpoint, ipEndpoint, ipEndpoint, ipEndpoint, ipEndpoint, null, 0, 0,
			0, 0, 0, -1, -1, Guid.Empty, 0, false);
	}

	private static object[] AllowedNodeRemovalStates => DeadNodeRemoval.AllowedNodeRemovalStates;
	private static object[] DisallowedNodeRemovalStates => DeadNodeRemoval.DisallowedNodeRemovalStates;

	[Test]
	[TestCaseSource(nameof(AllowedNodeRemovalStates))]
	public void
		should_remove_dead_members_which_have_timestamps_older_than_the_allowed_dead_member_removal_timeout(VNodeState currentRole) {
		var timeProvider = new FakeTimeProvider();
		var deadMemberRemovalTimeout = TimeSpan.FromSeconds(1);

		var nodeToBeRemoved =
			TestNodeFor(2, isAlive: false, timeProvider.UtcNow.Subtract(deadMemberRemovalTimeout));

		var updatedCluster = GossipServiceBase.UpdateCluster(new ClusterInfo(
				TestNodeFor(1, isAlive: false, timeProvider.UtcNow),
				nodeToBeRemoved,
				TestNodeFor(3, isAlive: false, timeProvider.UtcNow)), info => info,
			timeProvider, deadMemberRemovalTimeout, currentRole);

		Assert.That(updatedCluster.Members, Has.Length.EqualTo(2));
		Assert.IsFalse(updatedCluster.Members.Any(x => x.Is(nodeToBeRemoved.HttpEndPoint)));
	}

	[Test]
	[TestCaseSource(nameof(AllowedNodeRemovalStates))]
	public void
		should_not_remove_alive_members_which_have_timestamps_older_than_the_allowed_dead_member_removal_timeout(VNodeState currentRole) {
		var timeProvider = new FakeTimeProvider();
		var deadMemberRemovalTimeout = TimeSpan.FromSeconds(1);

		var nodeToNotBeRemoved =
			TestNodeFor(2, isAlive: true, timeProvider.UtcNow.Subtract(deadMemberRemovalTimeout));

		var updatedCluster = GossipServiceBase.UpdateCluster(new ClusterInfo(
				TestNodeFor(1, isAlive: false, timeProvider.UtcNow),
				nodeToNotBeRemoved,
				TestNodeFor(3, isAlive: false, timeProvider.UtcNow)), info => info,
			timeProvider, deadMemberRemovalTimeout, currentRole);

		Assert.That(updatedCluster.Members, Has.Length.EqualTo(3));
		Assert.IsTrue(updatedCluster.Members.Any(x => x.Is(nodeToNotBeRemoved.HttpEndPoint)));
	}

	[Test]
	[TestCaseSource(nameof(DisallowedNodeRemovalStates))]
	public void
		should_not_remove_dead_members_which_have_timestamps_older_than_the_allowed_dead_member_removal_timeout_and_the_current_role_is_unknown(VNodeState currentRole) {
		var timeProvider = new FakeTimeProvider();
		var deadMemberRemovalTimeout = TimeSpan.FromSeconds(1);

		var nodeToNotBeRemoved =
			TestNodeFor(2, isAlive: false, timeProvider.UtcNow.Subtract(deadMemberRemovalTimeout));

		var updatedCluster = GossipServiceBase.UpdateCluster(new ClusterInfo(
				TestNodeFor(1, isAlive: false, timeProvider.UtcNow),
				nodeToNotBeRemoved,
				TestNodeFor(3, isAlive: false, timeProvider.UtcNow)), info => info,
			timeProvider, deadMemberRemovalTimeout, currentRole);

		Assert.That(updatedCluster.Members, Has.Length.EqualTo(3));
		Assert.IsTrue(updatedCluster.Members.Any(x => x.Is(nodeToNotBeRemoved.HttpEndPoint)));
	}
}

public class when_merging_clusters {
	private static MemberInfo TestNodeFor(int identifier, bool isAlive, DateTime timeStamp, VNodeState nodeState) {
		var ipEndpoint = new IPEndPoint(IPAddress.Loopback, identifier);
		return MemberInfo.ForVNode(Guid.NewGuid(), timeStamp, nodeState, isAlive,
			ipEndpoint, ipEndpoint, ipEndpoint, ipEndpoint, ipEndpoint, null, 0, 0,
			0, 0, 0, -1, -1, Guid.Empty, 0, false);
	}

	private static object[] AllowedNodeRemovalStates => DeadNodeRemoval.AllowedNodeRemovalStates;
	private static object[] DisallowedNodeRemovalStates => DeadNodeRemoval.DisallowedNodeRemovalStates;

	[Test]
	[TestCaseSource(nameof(AllowedNodeRemovalStates))]
	public void
		should_remove_dead_members_which_have_timestamps_older_than_the_allowed_dead_member_removal_timeout(VNodeState currentRole) {
		var timeProvider = new FakeTimeProvider();
		var deadMemberRemovalTimeout = TimeSpan.FromSeconds(1);
		var allowedTimeDifference = TimeSpan.FromMilliseconds(1000);

		var me = TestNodeFor(1, isAlive: false, timeProvider.UtcNow, currentRole);
		var nodeToBeRemoved =
			TestNodeFor(2, isAlive: false, timeProvider.UtcNow.Subtract(deadMemberRemovalTimeout), VNodeState.Initializing);
		var peer = TestNodeFor(3, isAlive: false, timeProvider.UtcNow, VNodeState.Initializing);
		var cluster = new ClusterInfo(me, nodeToBeRemoved, peer);

		var updatedCluster = GossipServiceBase.MergeClusters(
			cluster, cluster, me.HttpEndPoint,
			info => info, timeProvider.UtcNow, me, peer.InstanceId,
			allowedTimeDifference, deadMemberRemovalTimeout);

		Assert.That(updatedCluster.Members, Has.Length.EqualTo(2));
		Assert.IsFalse(updatedCluster.Members.Any(x => x.Is(nodeToBeRemoved.HttpEndPoint)));
	}

	[Test]
	[TestCaseSource(nameof(AllowedNodeRemovalStates))]
	public void
		should_not_remove_alive_members_which_have_timestamps_older_than_the_allowed_dead_member_removal_timeout(VNodeState currentRole) {
		var timeProvider = new FakeTimeProvider();
		var deadMemberRemovalTimeout = TimeSpan.FromSeconds(1);
		var allowedTimeDifference = TimeSpan.FromMilliseconds(1000);

		var me = TestNodeFor(1, isAlive: true, timeProvider.UtcNow, currentRole);
		var nodeToBeRemoved = TestNodeFor(2, isAlive: true, timeProvider.UtcNow.Subtract(deadMemberRemovalTimeout), VNodeState.Initializing);
		var peer = TestNodeFor(3, isAlive: true, timeProvider.UtcNow, VNodeState.Initializing);
		var cluster = new ClusterInfo(me, nodeToBeRemoved, peer);

		var updatedCluster = GossipServiceBase.MergeClusters(
			cluster, cluster, me.HttpEndPoint,
			info => info, timeProvider.UtcNow, me, peer.InstanceId,
			allowedTimeDifference, deadMemberRemovalTimeout);

		Assert.That(updatedCluster.Members, Has.Length.EqualTo(3));
		Assert.IsTrue(updatedCluster.Members.Any(x => x.Is(nodeToBeRemoved.HttpEndPoint)));
	}

	[Test]
	[TestCaseSource(nameof(DisallowedNodeRemovalStates))]
	public void
		should_not_remove_dead_members_which_have_timestamps_older_than_the_allowed_dead_member_removal_timeout_and_current_role_is_unknown(
			VNodeState currentRole) {
		var timeProvider = new FakeTimeProvider();
		var deadMemberRemovalTimeout = TimeSpan.FromSeconds(1);
		var allowedTimeDifference = TimeSpan.FromMilliseconds(1000);

		var me = TestNodeFor(1, isAlive: false, timeProvider.UtcNow, currentRole);
		var nodeToBeRemoved =
			TestNodeFor(2, isAlive: false, timeProvider.UtcNow.Subtract(deadMemberRemovalTimeout),
				VNodeState.Initializing);
		var peer = TestNodeFor(3, isAlive: false, timeProvider.UtcNow, VNodeState.Initializing);
		var cluster = new ClusterInfo(me, nodeToBeRemoved, peer);

		var updatedCluster = GossipServiceBase.MergeClusters(
			cluster, cluster, me.HttpEndPoint,
			info => info, timeProvider.UtcNow, me, peer.InstanceId,
			allowedTimeDifference, deadMemberRemovalTimeout);

		Assert.That(updatedCluster.Members, Has.Length.EqualTo(3));
		Assert.IsTrue(updatedCluster.Members.Any(x => x.Is(nodeToBeRemoved.HttpEndPoint)));
	}
}

public class DeadNodeRemoval {
	public static readonly object[] DisallowedNodeRemovalStates = {
		new object[] {VNodeState.Initializing},
		new object[] {VNodeState.DiscoverLeader},
		new object[] {VNodeState.Unknown},
		new object[] {VNodeState.ReadOnlyLeaderless}
	};
	public static readonly object[] AllowedNodeRemovalStates = {
		new object[] {VNodeState.PreReplica},
		new object[] {VNodeState.CatchingUp},
		new object[] {VNodeState.Clone},
		new object[] {VNodeState.Follower},
		new object[] {VNodeState.PreLeader},
		new object[] {VNodeState.Leader},
		new object[] {VNodeState.ShuttingDown},
		new object[] {VNodeState.Shutdown},
		new object[] {VNodeState.PreReadOnlyReplica},
		new object[] {VNodeState.ReadOnlyReplica},
		new object[] {VNodeState.ResigningLeader},
	};
}
