// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Net;
using EventStore.Core.Bus;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Services.TimeService;
using EventStore.Core.TransactionLog.Checkpoint;
using FluentAssertions;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.ElectionsService;

public abstract class ElectionsFixture {
	protected readonly VNodeInfo _node;
	protected readonly VNodeInfo _nodeTwo;
	protected readonly VNodeInfo _nodeThree;
	protected FakeTimeProvider _timeProvider;
	protected Core.Services.ElectionsService _sut;
	private ISubscriber _bus;
	protected FakePublisher _publisher;
	protected Guid _epochId;
	protected TimeSpan LeaderElectionProgressTimeout = TimeSpan.FromMilliseconds(1_000);

	protected static Func<int, VNodeInfo> NodeFactory = (id) => new VNodeInfo(
		Guid.Parse($"00000000-0000-0000-0000-00000000000{id}"), id,
		new IPEndPoint(IPAddress.Loopback, id),
		new IPEndPoint(IPAddress.Loopback, id),
		new IPEndPoint(IPAddress.Loopback, id),
		new IPEndPoint(IPAddress.Loopback, id),
		new IPEndPoint(IPAddress.Loopback, id), false);

	protected static readonly Func<VNodeInfo, DateTime, VNodeState, bool, int, Guid, int, MemberInfo> MemberInfoFromVNode =
		(nodeInfo, timestamp, state, isAlive, epochNumber, epochId, priority) => MemberInfo.ForVNode(
			nodeInfo.InstanceId, timestamp, state, isAlive,
			nodeInfo.InternalTcp,
			nodeInfo.InternalSecureTcp, nodeInfo.ExternalTcp, nodeInfo.ExternalSecureTcp,
			nodeInfo.HttpEndPoint, null, 0, 0,
			0, 0, 0, 0, epochNumber, epochId, priority,
			nodeInfo.IsReadOnlyReplica);

	protected ElectionsFixture(VNodeInfo node, VNodeInfo nodeTwo, VNodeInfo nodeThree) {
		_timeProvider = new FakeTimeProvider();
		_publisher = new FakePublisher();
		_bus = new InMemoryBus("Test");
		_epochId = Guid.NewGuid();

		_node = node;
		_nodeTwo = nodeTwo;
		_nodeThree = nodeThree;
		_sut = new Core.Services.ElectionsService(_publisher,
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0), 3,
			new InMemoryCheckpoint(0),
			new InMemoryCheckpoint(0),
			new InMemoryCheckpoint(-1),
			new FakeEpochManager(), () => 0L, 0, _timeProvider,
			LeaderElectionProgressTimeout);
		_sut.SubscribeMessages(_bus);
	}
}

public class when_starting_elections : ElectionsFixture {
	public when_starting_elections()
		: base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_send_view_change_to_other_members() {
		_sut.Handle(new ElectionMessage.StartElections());

		var expected = new Message[] {
			new GrpcMessage.SendOverGrpc(_nodeTwo.HttpEndPoint,
				new ElectionMessage.ViewChange(_node.InstanceId, _node.HttpEndPoint, 0),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
			new GrpcMessage.SendOverGrpc(_nodeThree.HttpEndPoint,
				new ElectionMessage.ViewChange(_node.InstanceId, _node.HttpEndPoint, 0),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
			TimerMessage.Schedule.Create(
				LeaderElectionProgressTimeout,
				_publisher,
				new ElectionMessage.ElectionsTimedOut(0)),
		};

		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_system_init : ElectionsFixture {
	public when_system_init()
		: base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new SystemMessage.SystemInit());
	}

	[Test]
	public void should_start_view_change_proof_timer() {
		var expected = new Message[] {
			TimerMessage.Schedule.Create(
				Core.Services.ElectionsService.SendViewChangeProofInterval,
				_publisher,
				new ElectionMessage.SendViewChangeProof()),
		};
		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_starting_elections_after_elections_have_started : ElectionsFixture {
	public when_starting_elections_after_elections_have_started()
		: base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_starting_elections() {
		_sut.Handle(new ElectionMessage.StartElections());
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.StartElections());

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_node_is_shutting_down_and_starting_elections : ElectionsFixture {
	public when_node_is_shutting_down_and_starting_elections()
		: base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_starting_elections() {
		_sut.Handle(new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), false, false));
		_sut.Handle(new ElectionMessage.StartElections());

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_node_is_shutting_down_and_elections_timed_out : ElectionsFixture {
	public when_node_is_shutting_down_and_elections_timed_out() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_elections_timed_out() {
		_sut.Handle(new ElectionMessage.StartElections());
		_publisher.Messages.Clear();
		_sut.Handle(new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), false, false));
		_sut.Handle(new ElectionMessage.ElectionsTimedOut(0));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_elections_timeout_for_a_different_view_than_last_attempted_view : ElectionsFixture {
	public when_elections_timeout_for_a_different_view_than_last_attempted_view() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_elections_timed_out() {
		_sut.Handle(new ElectionMessage.StartElections());
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.ElectionsTimedOut(1));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_elections_timeout : ElectionsFixture {
	public when_elections_timeout() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_send_new_view_change_to_other_members() {
		var view = 0;
		var newView = view + 1;
		_sut.Handle(new ElectionMessage.StartElections());
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.ElectionsTimedOut(view));

		var expected = new Message[] {
			new GrpcMessage.SendOverGrpc(_nodeTwo.HttpEndPoint,
				new ElectionMessage.ViewChange(_node.InstanceId, _node.HttpEndPoint, newView),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
			new GrpcMessage.SendOverGrpc(_nodeThree.HttpEndPoint,
				new ElectionMessage.ViewChange(_node.InstanceId, _node.HttpEndPoint, newView),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
			TimerMessage.Schedule.Create(
				LeaderElectionProgressTimeout,
				_publisher,
				new ElectionMessage.ElectionsTimedOut(1)),
		};
		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_node_is_shutting_down_and_view_change_proof_is_triggered : ElectionsFixture {
	public when_node_is_shutting_down_and_view_change_proof_is_triggered() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_view_change_proof() {
		_sut.Handle(new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), false, false));
		_sut.Handle(new ElectionMessage.SendViewChangeProof());

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_view_change_proof_is_triggered_and_the_first_election_has_not_completed : ElectionsFixture {
	public when_view_change_proof_is_triggered_and_the_first_election_has_not_completed() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_schedule_another_view_change_proof() {
		_sut.Handle(new ElectionMessage.StartElections());
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.SendViewChangeProof());

		var expected = new Message[] {
			TimerMessage.Schedule.Create(
				Core.Services.ElectionsService.SendViewChangeProofInterval,
				_publisher,
				new ElectionMessage.SendViewChangeProof()),
		};
		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_view_change_proof_is_triggered_and_the_first_election_has_completed : ElectionsFixture {
	public when_view_change_proof_is_triggered_and_the_first_election_has_completed() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_send_view_change_proof_to_other_members() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0, 0,
			_epochId, Guid.Empty, 0, 0, 0, 0, new ClusterInfo()));
		var proposalHttpMessage = _publisher.Messages.OfType<GrpcMessage.SendOverGrpc>()
			.FirstOrDefault(x => x.Message is ElectionMessage.Proposal);
		var proposalMessage = (ElectionMessage.Proposal)proposalHttpMessage.Message;
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			proposalMessage.LeaderId, proposalMessage.LeaderHttpEndPoint, 0));
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.SendViewChangeProof());

		var expected = new Message[] {
			new GrpcMessage.SendOverGrpc(_nodeTwo.HttpEndPoint,
				new ElectionMessage.ViewChangeProof(_node.InstanceId, _node.HttpEndPoint, 0),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
			new GrpcMessage.SendOverGrpc(_nodeThree.HttpEndPoint,
				new ElectionMessage.ViewChangeProof(_node.InstanceId, _node.HttpEndPoint, 0),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
			TimerMessage.Schedule.Create(
				Core.Services.ElectionsService.SendViewChangeProofInterval,
				_publisher,
				new ElectionMessage.SendViewChangeProof()),
		};
		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_receiving_view_change_and_shutting_down : ElectionsFixture {
	public when_receiving_view_change_and_shutting_down() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_the_view_change() {
		_publisher.Messages.Clear();

		_sut.Handle(new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), false, false));
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, -2));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_view_change_and_idle : ElectionsFixture {
	public when_receiving_view_change_and_idle() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_the_view_change() {
		_publisher.Messages.Clear();

		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, -2));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_view_change_for_an_earlier_view_than_installed : ElectionsFixture {
	public when_receiving_view_change_for_an_earlier_view_than_installed() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_the_view_change() {
		_sut.Handle(new ElectionMessage.StartElections());
		_publisher.Messages.Clear();

		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, -2));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_a_view_change_for_a_later_view_than_last_attempted_view : ElectionsFixture {
	public when_receiving_a_view_change_for_a_later_view_than_last_attempted_view() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_send_view_change_to_other_members() {
		_sut.Handle(new ElectionMessage.StartElections());
		_publisher.Messages.Clear();

		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 10));

		var expected = new Message[] {
			new GrpcMessage.SendOverGrpc(_nodeTwo.HttpEndPoint,
				new ElectionMessage.ViewChange(_node.InstanceId, _node.HttpEndPoint, 10),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
			new GrpcMessage.SendOverGrpc(_nodeThree.HttpEndPoint,
				new ElectionMessage.ViewChange(_node.InstanceId, _node.HttpEndPoint, 10),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
			TimerMessage.Schedule.Create(
				LeaderElectionProgressTimeout,
				_publisher,
				new ElectionMessage.ElectionsTimedOut(10)),
		};

		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_receiving_view_change_from_majority_and_acceptor_of_the_current_view : ElectionsFixture {
	public when_receiving_view_change_from_majority_and_acceptor_of_the_current_view() :
		base(NodeFactory(1), NodeFactory(2), NodeFactory(3)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_not_initiate_prepare_phase() {
		_sut.Handle(new ElectionMessage.StartElections());
		_publisher.Messages.Clear();

		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_view_change_from_majority : ElectionsFixture {
	public when_receiving_view_change_from_majority() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_send_prepares_to_other_members() {
		_sut.Handle(new ElectionMessage.StartElections());
		_publisher.Messages.Clear();

		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));

		var expected = new Message[] {
			new GrpcMessage.SendOverGrpc(_nodeTwo.HttpEndPoint,
				new ElectionMessage.Prepare(_node.InstanceId, _node.HttpEndPoint, 0),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
			new GrpcMessage.SendOverGrpc(_nodeThree.HttpEndPoint,
				new ElectionMessage.Prepare(_node.InstanceId, _node.HttpEndPoint, 0),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout))
		};
		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_receiving_a_prepare_when_node_is_shutting_down : ElectionsFixture {
	public when_receiving_a_prepare_when_node_is_shutting_down() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_the_prepare() {
		_sut.Handle(new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), false, false));
		_sut.Handle(new ElectionMessage.Prepare(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_publisher.Messages.Clear();
		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_a_prepare_for_the_same_node : ElectionsFixture {
	public when_receiving_a_prepare_for_the_same_node() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_the_prepare() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.Prepare(_node.InstanceId, _node.HttpEndPoint, 0));
		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_a_prepare_not_for_the_last_attempted_view : ElectionsFixture {
	public when_receiving_a_prepare_not_for_the_last_attempted_view() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_the_prepare() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.Prepare(_nodeThree.InstanceId, _nodeThree.HttpEndPoint, 1));
		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_a_prepare_from_an_unknown_node : ElectionsFixture {
	public when_receiving_a_prepare_from_an_unknown_node() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_the_prepare() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.Prepare(Guid.NewGuid(), new IPEndPoint(IPAddress.Loopback, 1114), 0));
		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_a_prepare : ElectionsFixture {
	private readonly ClusterInfo _clusterInfo;
	public when_receiving_a_prepare() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_clusterInfo = new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0));
		_sut.Handle(new GossipMessage.GossipUpdated(_clusterInfo));
	}

	[Test]
	public void should_reply_with_prepare_ok() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.Prepare(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));

		var expected = new Message[] {
			new GrpcMessage.SendOverGrpc(_nodeTwo.HttpEndPoint,
				new ElectionMessage.PrepareOk(0,
					_node.InstanceId, _node.HttpEndPoint, -1, -1, Guid.Empty, Guid.Empty, 0, 0, 0, 0, _clusterInfo),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
		};
		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_receiving_prepare_ok_and_node_is_shutting_down : ElectionsFixture {
	public when_receiving_prepare_ok_and_node_is_shutting_down() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_prepare_ok() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_publisher.Messages.Clear();
		_sut.Handle(new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), false, false));

		_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0, 0,
			_epochId, Guid.Empty, 0, 0, 0, 0, new ClusterInfo()));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_prepare_ok_and_elections_have_not_started : ElectionsFixture {
	public when_receiving_prepare_ok_and_elections_have_not_started() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_prepare_ok() {
		_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0, 0,
			_epochId, Guid.Empty, 0, 0, 0, 0, new ClusterInfo()));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_prepare_ok_for_not_the_current_attempted_view : ElectionsFixture {
	public when_receiving_prepare_ok_for_not_the_current_attempted_view() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_prepare_ok() {
		_sut.Handle(new ElectionMessage.StartElections());
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.PrepareOk(-1, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0, 0,
			_epochId, Guid.Empty, 0, 0, 0, 0, new ClusterInfo()));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_a_duplicate_prepare_ok : ElectionsFixture {
	public when_receiving_a_duplicate_prepare_ok() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_prepare_ok() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_publisher.Messages.Clear();

		_sut.Handle(new ElectionMessage.PrepareOk(0, _node.InstanceId, _node.HttpEndPoint, 0, 0,
			_epochId, Guid.Empty, 0, 0, 0, 0, new ClusterInfo()));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_majority_prepare_ok : ElectionsFixture {
	public when_receiving_majority_prepare_ok() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_send_proposal_to_other_members() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_publisher.Messages.Clear();

		_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0, 0,
			_epochId, Guid.Empty, 0, 0, 0, 0, new ClusterInfo()));

		var proposalHttpMessage = _publisher.Messages.OfType<GrpcMessage.SendOverGrpc>()
			.FirstOrDefault(x => x.Message is ElectionMessage.Proposal);
		var proposalMessage = (ElectionMessage.Proposal)proposalHttpMessage.Message;

		var expected = new Message[] {
			new GrpcMessage.SendOverGrpc(_nodeTwo.HttpEndPoint,
				new ElectionMessage.Proposal(_node.InstanceId, _node.HttpEndPoint,
					proposalMessage.LeaderId,
					proposalMessage.LeaderHttpEndPoint, 0, 0, 0, _epochId, Guid.Empty, 0, 0, 0, 0),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
			new GrpcMessage.SendOverGrpc(_nodeThree.HttpEndPoint,
				new ElectionMessage.Proposal(_node.InstanceId, _node.HttpEndPoint,
					proposalMessage.LeaderId,
					proposalMessage.LeaderHttpEndPoint, 0, 0, 0, _epochId, Guid.Empty, 0, 0, 0, 0),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout))
		};
		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_node_is_shutting_down_and_receive_a_proposal : ElectionsFixture {
	public when_node_is_shutting_down_and_receive_a_proposal() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_proposal() {
		_sut.Handle(new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), false, false));
		_sut.Handle(new ElectionMessage.Proposal(_node.InstanceId, _node.HttpEndPoint,
			_node.InstanceId,
			_node.HttpEndPoint, 0, 0, 0, _epochId, Guid.Empty, 0, 0, 0, 0));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_a_proposal_from_the_same_node : ElectionsFixture {
	public when_receiving_a_proposal_from_the_same_node() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_proposal() {
		_sut.Handle(new ElectionMessage.Proposal(_node.InstanceId, _node.HttpEndPoint,
			_node.InstanceId,
			_node.HttpEndPoint, 0, 0, 0, _epochId, Guid.Empty, 0, 0, 0, 0));

		Assert.IsEmpty(_publisher.Messages,
			"Nodes do not send proposals to themselves, they accept their own proposal implicitly.");
	}
}

public class when_receiving_a_proposal_as_the_acceptor_of_the_current_view : ElectionsFixture {
	public when_receiving_a_proposal_as_the_acceptor_of_the_current_view() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_proposal() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0, 0,
			_epochId, Guid.Empty, 0, 0, 0, 0, new ClusterInfo()));
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.Proposal(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			_node.InstanceId,
			_node.HttpEndPoint, 0, 0, 0, _epochId, Guid.Empty, 0, 0, 0, 0));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_a_proposal_not_for_the_current_installed_view : ElectionsFixture {
	public when_receiving_a_proposal_not_for_the_current_installed_view() :
		base(NodeFactory(1), NodeFactory(2), NodeFactory(3)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_proposal() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.Prepare(_nodeThree.InstanceId, _nodeThree.HttpEndPoint, 0));
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.Proposal(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			_node.InstanceId,
			_node.HttpEndPoint, 1, 0, 0, _epochId, Guid.Empty, 0, 0, 0, 0));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_a_proposal_from_an_unknown_node : ElectionsFixture {
	public when_receiving_a_proposal_from_an_unknown_node() :
		base(NodeFactory(1), NodeFactory(2), NodeFactory(3)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_proposal() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.Prepare(_nodeThree.InstanceId, _nodeThree.HttpEndPoint, 0));
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.Proposal(Guid.NewGuid(), new IPEndPoint(IPAddress.Loopback, 4),
			_node.InstanceId,
			_node.HttpEndPoint, 0, 0, 0, _epochId, Guid.Empty, 0, 0, 0, 0));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_a_proposal_for_an_unknown_node : ElectionsFixture {
	public when_receiving_a_proposal_for_an_unknown_node() :
		base(NodeFactory(1), NodeFactory(2), NodeFactory(3)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_proposal() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.Prepare(_nodeThree.InstanceId, _nodeThree.HttpEndPoint, 0));
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.Proposal(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			Guid.NewGuid(),
			new IPEndPoint(IPAddress.Loopback, 4), 0, 0, 0, _epochId, Guid.Empty, 0, 0, 0, 0));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_a_proposal_as_acceptor : ElectionsFixture {
	public when_receiving_a_proposal_as_acceptor() :
		base(NodeFactory(1), NodeFactory(2), NodeFactory(3)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_send_an_acceptance_to_other_members() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.Prepare(_nodeThree.InstanceId, _nodeThree.HttpEndPoint, 0));
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.Proposal(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			_nodeThree.InstanceId,
			_nodeThree.HttpEndPoint, 0, 0, 0, _epochId, Guid.Empty, 0, 0, 0, 0));

		var expected = new Message[] {
			new ElectionMessage.ElectionsDone(0,0,
				MemberInfo.ForVNode(
					_nodeThree.InstanceId, _timeProvider.UtcNow, VNodeState.Unknown, true,
					_nodeThree.InternalTcp,
					_nodeThree.InternalSecureTcp, _nodeThree.ExternalTcp, _nodeThree.ExternalSecureTcp,
					_nodeThree.HttpEndPoint, null, 0, 0, 0, 0, 0, 0, 0, _epochId, 0,
					_nodeThree.IsReadOnlyReplica)),
			new GrpcMessage.SendOverGrpc(_nodeThree.HttpEndPoint,
				new ElectionMessage.Accept(_node.InstanceId, _node.HttpEndPoint,
					_nodeThree.InstanceId,
					_nodeThree.HttpEndPoint, 0),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
			new GrpcMessage.SendOverGrpc(_nodeTwo.HttpEndPoint,
				new ElectionMessage.Accept(_node.InstanceId, _node.HttpEndPoint,
					_nodeThree.InstanceId,
					_nodeThree.HttpEndPoint, 0),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
		};
		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_node_is_shutting_down_and_receiving_accept : ElectionsFixture {
	public when_node_is_shutting_down_and_receiving_accept() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_accept() {
		_sut.Handle(new SystemMessage.BecomeShuttingDown(Guid.NewGuid(), false, false));
		_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			_nodeThree.InstanceId, _nodeThree.HttpEndPoint, 0));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_accept_for_not_the_current_installed_view : ElectionsFixture {
	public when_receiving_accept_for_not_the_current_installed_view() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_accept() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0, 0,
			_epochId, Guid.Empty, 0, 0, 0, 0, new ClusterInfo()));
		_publisher.Messages.Clear();

		_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			_nodeThree.InstanceId, _nodeThree.HttpEndPoint, 1));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_accept_without_a_leader_having_been_proposed : ElectionsFixture {
	public when_receiving_accept_without_a_leader_having_been_proposed() :
		base(NodeFactory(1), NodeFactory(2), NodeFactory(3)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_accept() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.Prepare(_nodeThree.InstanceId, _nodeThree.HttpEndPoint, 0));
		_publisher.Messages.Clear();

		_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			_nodeThree.InstanceId, _nodeThree.HttpEndPoint, 0));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_accept_and_leader_proposal_does_not_match_accept_leader : ElectionsFixture {
	public when_receiving_accept_and_leader_proposal_does_not_match_accept_leader() :
		base(NodeFactory(1), NodeFactory(2), NodeFactory(3)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_accept() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.Prepare(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.Proposal(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			Guid.NewGuid(),
			new IPEndPoint(IPAddress.Loopback, 4), 0, 0, 0, _epochId, Guid.Empty, 0, 0, 0, 0));
		_publisher.Messages.Clear();

		_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			_nodeThree.InstanceId, _nodeThree.HttpEndPoint, 0));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_receiving_majority_accept : ElectionsFixture {
	public when_receiving_majority_accept() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_complete_elections() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0, 0,
			_epochId, Guid.Empty, 0, 0, 0, 0, new ClusterInfo()));
		var proposalHttpMessage = _publisher.Messages.OfType<GrpcMessage.SendOverGrpc>()
			.FirstOrDefault(x => x.Message is ElectionMessage.Proposal);
		var proposalMessage = (ElectionMessage.Proposal)proposalHttpMessage.Message;
		_publisher.Messages.Clear();

		_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			proposalMessage.LeaderId, proposalMessage.LeaderHttpEndPoint, 0));

		var expected = new Message[] {
			new ElectionMessage.ElectionsDone(0,0,
				MemberInfo.ForVNode(
					_nodeTwo.InstanceId, _timeProvider.UtcNow, VNodeState.Unknown, true,
					_nodeTwo.InternalTcp,
					_nodeTwo.InternalSecureTcp, _nodeTwo.ExternalTcp, _nodeTwo.ExternalSecureTcp,
					_nodeTwo.HttpEndPoint, null, 0, 0, 0, 0, 0, 0, 0, _epochId, 0,
					_nodeTwo.IsReadOnlyReplica)),
		};
		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_elections_timeout_and_not_electing_leader : ElectionsFixture {
	public when_elections_timeout_and_not_electing_leader() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_timeout() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0, 0,
			_epochId, Guid.Empty, 0, 0, 0, 0, new ClusterInfo()));
		var proposalHttpMessage = _publisher.Messages.OfType<GrpcMessage.SendOverGrpc>()
			.FirstOrDefault(x => x.Message is ElectionMessage.Proposal);
		var proposalMessage = (ElectionMessage.Proposal)proposalHttpMessage.Message;
		_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			proposalMessage.LeaderId, proposalMessage.LeaderHttpEndPoint, 0));
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.ElectionsTimedOut(0));

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_updating_node_priority : ElectionsFixture {
	public when_updating_node_priority() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_broadcast_node_priority_update() {
		var nodePriority = 5;
		_sut.Handle(new ClientMessage.SetNodePriority(nodePriority));

		var expected = new Message[] {
			new GossipMessage.UpdateNodePriority(nodePriority)
		};
		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_resigning_node_and_node_is_not_the_current_leader : ElectionsFixture {
	public when_resigning_node_and_node_is_not_the_current_leader() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_ignore_resign_node_message() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0, 0,
			_epochId, Guid.Empty, 0, 0, 0, 0, new ClusterInfo()));
		var proposalHttpMessage = _publisher.Messages.OfType<GrpcMessage.SendOverGrpc>()
			.FirstOrDefault(x => x.Message is ElectionMessage.Proposal);
		var proposalMessage = (ElectionMessage.Proposal)proposalHttpMessage.Message;
		_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			proposalMessage.LeaderId, proposalMessage.LeaderHttpEndPoint, 0));
		_publisher.Messages.Clear();
		_sut.Handle(new ClientMessage.ResignNode());

		Assert.IsEmpty(_publisher.Messages);
	}
}

public class when_resigning_node_and_is_the_current_leader : ElectionsFixture {
	public when_resigning_node_and_is_the_current_leader() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_initiate_leader_resignation_and_inform_other_nodes() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, -1, 0,
			_epochId, Guid.Empty, -1, -1, -1, -1, new ClusterInfo()));
		_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			_node.InstanceId, _node.HttpEndPoint, 0));
		_publisher.Messages.Clear();

		_sut.Handle(new ClientMessage.ResignNode());

		var expected = new Message[] {
			new GrpcMessage.SendOverGrpc(_nodeTwo.HttpEndPoint,
				new ElectionMessage.LeaderIsResigning(_node.InstanceId, _node.HttpEndPoint),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
			new GrpcMessage.SendOverGrpc(_nodeThree.HttpEndPoint,
				new ElectionMessage.LeaderIsResigning(_node.InstanceId, _node.HttpEndPoint),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
		};
		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_receiving_leader_is_resigning : ElectionsFixture {
	public when_receiving_leader_is_resigning() :
		base(NodeFactory(1), NodeFactory(2), NodeFactory(3)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_reply_with_leader_is_resigning_ok() {
		_sut.Handle(new ElectionMessage.LeaderIsResigning(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint));

		var expected = new Message[] {
			new GrpcMessage.SendOverGrpc(_nodeTwo.HttpEndPoint,
				new ElectionMessage.LeaderIsResigningOk(
					_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
					_node.InstanceId, _node.HttpEndPoint),
				_timeProvider.LocalTime.Add(LeaderElectionProgressTimeout)),
		};
		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_resigning_node_and_majority_resigning_ok_received : ElectionsFixture {
	public when_resigning_node_and_majority_resigning_ok_received() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_initiate_leader_resignation() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, -1, 0,
			_epochId, Guid.Empty, -1, -1, -1, -1, new ClusterInfo()));
		_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			_node.InstanceId, _node.HttpEndPoint, 0));
		_sut.Handle(new ClientMessage.ResignNode());
		_publisher.Messages.Clear();

		_sut.Handle(new ElectionMessage.LeaderIsResigningOk(
			_node.InstanceId,
			_node.HttpEndPoint,
			_nodeTwo.InstanceId,
			_nodeTwo.HttpEndPoint));

		var expected = new Message[] {
			new SystemMessage.InitiateLeaderResignation(),
		};
		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_electing_a_leader_and_leader_node_resigned : ElectionsFixture {
	public when_electing_a_leader_and_leader_node_resigned() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void should_attempt_not_to_elect_previously_elected_leader() {
		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, -1, 0,
			_epochId, Guid.Empty, -1, -1, -1, -1, new ClusterInfo()));
		_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			_node.InstanceId, _node.HttpEndPoint, 0));
		_publisher.Messages.Clear();

		_sut.Handle(new ClientMessage.ResignNode());
		_sut.Handle(new ElectionMessage.LeaderIsResigningOk(
			_node.InstanceId,
			_node.HttpEndPoint,
			_nodeTwo.InstanceId,
			_nodeTwo.HttpEndPoint));
		_publisher.Messages.Clear();

		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 3));
		_sut.Handle(new ElectionMessage.PrepareOk(3, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0, 0,
			_epochId, Guid.Empty, 0, 0, 0, 0, new ClusterInfo()));
		var proposalHttpMessage = _publisher.Messages.OfType<GrpcMessage.SendOverGrpc>()
			.FirstOrDefault(x => x.Message is ElectionMessage.Proposal);
		var proposalMessage = (ElectionMessage.Proposal)proposalHttpMessage.Message;
		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			proposalMessage.LeaderId, proposalMessage.LeaderHttpEndPoint, 3));

		var expected = new Message[] {
			new ElectionMessage.ElectionsDone(3,1,
				MemberInfo.ForVNode(
					_nodeTwo.InstanceId, _timeProvider.UtcNow, VNodeState.Unknown, true,
					_nodeTwo.InternalTcp,
					_nodeTwo.InternalSecureTcp, _nodeTwo.ExternalTcp, _nodeTwo.ExternalSecureTcp,
					_nodeTwo.HttpEndPoint, null, 0, 0, 0, 0, 0, 0, 0, _epochId, 0,
					_nodeTwo.IsReadOnlyReplica)),
		};
		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_a_leader_is_found_during_leader_discovery : ElectionsFixture {
	public when_a_leader_is_found_during_leader_discovery() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {
		var info = MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0);
		_sut.Handle(new LeaderDiscoveryMessage.LeaderFound(info));
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			info,
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));
	}

	[Test]
	public void view_change_should_trigger_new_elections() {
		_sut.Handle(new ElectionMessage.ViewChange(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 3));
		_publisher.Messages.Clear();

		_sut.Handle(new ElectionMessage.PrepareOk(3, _nodeThree.InstanceId, _nodeThree.HttpEndPoint, 0, 0,
			_epochId, Guid.Empty, 0, 0, 0, 0, new ClusterInfo()));

		Assert.NotNull(_publisher.Messages.OfType<GrpcMessage.SendOverGrpc>().FirstOrDefault());
	}
}

public class when_the_elections_service_is_initialized_as_read_only_replica {
	[Test]
	public void should_throw_argument_exception() {
		var endpoint = new IPEndPoint(IPAddress.Loopback, 1234);
		var nodeInfo = MemberInfo.Initial(Guid.NewGuid(),
			DateTime.UtcNow, VNodeState.ReadOnlyLeaderless, true,
			endpoint,
			endpoint,
			endpoint,
			endpoint,
			endpoint, null, 0, 0,
			0,
			true);

		Assert.Throws<ArgumentException>(() => {
			new Core.Services.ElectionsService(new FakePublisher(), nodeInfo, 3,
				new InMemoryCheckpoint(0),
				new InMemoryCheckpoint(0),
				new InMemoryCheckpoint(-1),
				new FakeEpochManager(), () => 0L, 0, new FakeTimeProvider(),
				TimeSpan.FromMilliseconds(1_000));
		});
	}
}
public class when_electing_a_leader_and_prepare_ok_is_received_from_previous_leader_in_epoch_record : ElectionsFixture {
	public when_electing_a_leader_and_prepare_ok_is_received_from_previous_leader_in_epoch_record() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {

		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0))));

		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeThree.InstanceId, _nodeThree.HttpEndPoint, 0));

		_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeThree.InstanceId, _nodeThree.HttpEndPoint, 10, 0,
			_epochId, _nodeThree.InstanceId, 0, 0, 0, 0, new ClusterInfo()));

		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			_nodeThree.InstanceId, _nodeThree.HttpEndPoint, 0));
	}

	[Test]
	public void previous_leader_should_be_elected() {
		var expected = new Message[] {
			new ElectionMessage.ElectionsDone(0,0,
				MemberInfo.ForVNode(
					_nodeThree.InstanceId, _timeProvider.UtcNow, VNodeState.Unknown, true,
					_nodeThree.InternalTcp,
					_nodeThree.InternalSecureTcp, _nodeThree.ExternalTcp, _nodeThree.ExternalSecureTcp,
					_nodeThree.HttpEndPoint, null, 0, 0,
					0, 0, 0, 0, 0, _epochId, 0,
					_nodeThree.IsReadOnlyReplica)),
		};

		_publisher.Messages.Should().BeEquivalentTo(expected);
	}
}

public class when_electing_a_leader_and_prepare_ok_is_not_received_from_previous_leader_in_epoch_record : ElectionsFixture {
	public when_electing_a_leader_and_prepare_ok_is_not_received_from_previous_leader_in_epoch_record() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {

		//we mark the 3rd node as dead in _node's gossip so that it does not affect the results of the elections
		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, false, 0, _epochId, 0))));

		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeThree.InstanceId, _nodeThree.HttpEndPoint, 0));
	}

	public class
		and_previous_leader_is_present_and_alive_in_cluster_info_in_received_prepare_ok :
			when_electing_a_leader_and_prepare_ok_is_not_received_from_previous_leader_in_epoch_record {

		public and_previous_leader_is_present_and_alive_in_cluster_info_in_received_prepare_ok() {
			_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 10, 0,
				_epochId, _nodeThree.InstanceId, 0, 0, 0, 0,
				new ClusterInfo(
					MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0)
				)));

			_publisher.Messages.Clear();
		}

		[Test]
		public void previous_leader_should_be_proposed() {
			Assert.AreEqual(_nodeThree.InstanceId, _sut.LeaderProposalId);
		}
	}

	public class
		and_previous_leader_is_present_and_alive_but_has_lower_priority_in_cluster_info_in_received_prepare_ok :
			when_electing_a_leader_and_prepare_ok_is_not_received_from_previous_leader_in_epoch_record {

		public and_previous_leader_is_present_and_alive_but_has_lower_priority_in_cluster_info_in_received_prepare_ok() {
			_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 10, 0,
				_epochId, _nodeThree.InstanceId, 0, 0, 0, 0,
				new ClusterInfo(
					MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, -1)
				)));

			_publisher.Messages.Clear();
		}

		[Test]
		public void previous_leader_should_be_proposed() {
			Assert.AreEqual(_nodeThree.InstanceId, _sut.LeaderProposalId);
		}
	}

	public class
		and_previous_leader_is_present_and_alive_in_cluster_info_in_received_prepare_ok_but_is_resigning :
			when_electing_a_leader_and_prepare_ok_is_not_received_from_previous_leader_in_epoch_record {

		public and_previous_leader_is_present_and_alive_in_cluster_info_in_received_prepare_ok_but_is_resigning() {
			_sut.Handle(new ElectionMessage.LeaderIsResigning(_nodeThree.InstanceId, _nodeThree.HttpEndPoint));

			_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 10, 0,
				_epochId, _nodeThree.InstanceId, 0, 0, 0, 0,
				new ClusterInfo(
					MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0)
				)));

			_publisher.Messages.Clear();
			_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
				_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		}

		[Test]
		public void previous_leader_should_not_be_elected() {
			var expected = new Message[] {
				new ElectionMessage.ElectionsDone(0,0,
					MemberInfo.ForVNode(
						_nodeTwo.InstanceId, _timeProvider.UtcNow, VNodeState.Unknown, true,
						_nodeTwo.InternalTcp,
						_nodeTwo.InternalSecureTcp, _nodeTwo.ExternalTcp, _nodeTwo.ExternalSecureTcp,
						_nodeTwo.HttpEndPoint, null, 0, 0,
						0, 0, 0, 0, 0, _epochId, 0,
						_nodeTwo.IsReadOnlyReplica)),
			};

			_publisher.Messages.Should().BeEquivalentTo(expected);
		}
	}

	public class
		and_previous_leader_is_present_and_alive_in_cluster_info_in_received_prepare_ok_and_epoch_number_matches_but_not_epoch_id :
			when_electing_a_leader_and_prepare_ok_is_not_received_from_previous_leader_in_epoch_record {

		public and_previous_leader_is_present_and_alive_in_cluster_info_in_received_prepare_ok_and_epoch_number_matches_but_not_epoch_id() {
			_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 10, 0,
				_epochId, _nodeThree.InstanceId, 0, 0, 0, 0,
				new ClusterInfo(
					MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 10, Guid.NewGuid(), 0)
				)));

			_publisher.Messages.Clear();
			_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
				_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		}

		[Test]
		public void previous_leader_should_not_be_elected() {
			var expected = new Message[] {
				new ElectionMessage.ElectionsDone(0,0,
					MemberInfo.ForVNode(
						_nodeTwo.InstanceId, _timeProvider.UtcNow, VNodeState.Unknown, true,
						_nodeTwo.InternalTcp,
						_nodeTwo.InternalSecureTcp, _nodeTwo.ExternalTcp, _nodeTwo.ExternalSecureTcp,
						_nodeTwo.HttpEndPoint, null, 0, 0,
						0, 0, 0, 0, 0, _epochId, 0,
						_nodeTwo.IsReadOnlyReplica)),
			};

			_publisher.Messages.Should().BeEquivalentTo(expected);
		}
	}

	public class
		and_previous_leader_is_present_and_alive_in_cluster_info_in_received_prepare_ok_and_epoch_number_does_not_match :
			when_electing_a_leader_and_prepare_ok_is_not_received_from_previous_leader_in_epoch_record {
		private Guid _otherEpochId = Guid.NewGuid();

		public and_previous_leader_is_present_and_alive_in_cluster_info_in_received_prepare_ok_and_epoch_number_does_not_match() {
			_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 10, 0,
				_epochId, _nodeThree.InstanceId, 0, 0, 0, 0,
				new ClusterInfo(
					MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, true, 9, _otherEpochId , 0)
				)));

			_publisher.Messages.Clear();
		}

		[Test]
		public void previous_leader_should_be_proposed() {
			Assert.AreEqual(_nodeThree.InstanceId, _sut.LeaderProposalId);
		}
	}

	public class
		and_previous_leader_is_not_present_in_cluster_info_in_received_prepare_ok :
			when_electing_a_leader_and_prepare_ok_is_not_received_from_previous_leader_in_epoch_record {

		public and_previous_leader_is_not_present_in_cluster_info_in_received_prepare_ok() {
			_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 10, 0,
				_epochId, _nodeThree.InstanceId, 0, 0, 0, 0, new ClusterInfo()));

			_publisher.Messages.Clear();
			_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
				_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		}

		[Test]
		public void previous_leader_should_not_be_elected() {
			var expected = new Message[] {
				new ElectionMessage.ElectionsDone(0,0,
					MemberInfo.ForVNode(
						_nodeTwo.InstanceId, _timeProvider.UtcNow, VNodeState.Unknown, true,
						_nodeTwo.InternalTcp,
						_nodeTwo.InternalSecureTcp, _nodeTwo.ExternalTcp, _nodeTwo.ExternalSecureTcp,
						_nodeTwo.HttpEndPoint, null, 0, 0,
						0, 0, 0, 0, 0, _epochId, 0,
						_nodeTwo.IsReadOnlyReplica)),
			};

			_publisher.Messages.Should().BeEquivalentTo(expected);
		}
	}

	public class
		and_previous_leader_is_present_but_dead_in_cluster_info_in_received_prepare_ok :
			when_electing_a_leader_and_prepare_ok_is_not_received_from_previous_leader_in_epoch_record {

		public and_previous_leader_is_present_but_dead_in_cluster_info_in_received_prepare_ok() {
			_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 10, 0,
				_epochId, _nodeThree.InstanceId, 0, 0, 0, 0,
				new ClusterInfo(
					MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, false, 0, _epochId, 0)
				)));

			_publisher.Messages.Clear();
			_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
				_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint, 0));
		}

		[Test]
		public void previous_leader_should_not_be_elected() {
			var expected = new Message[] {
				new ElectionMessage.ElectionsDone(0,0,
					MemberInfo.ForVNode(
						_nodeTwo.InstanceId, _timeProvider.UtcNow, VNodeState.Unknown, true,
						_nodeTwo.InternalTcp,
						_nodeTwo.InternalSecureTcp, _nodeTwo.ExternalTcp, _nodeTwo.ExternalSecureTcp,
						_nodeTwo.HttpEndPoint, null, 0, 0,
						0, 0, 0, 0, 0, _epochId, 0,
						_nodeTwo.IsReadOnlyReplica)),
			};

			_publisher.Messages.Should().BeEquivalentTo(expected);
		}
	}
}

public class when_electing_a_leader_and_best_candidate_node_is_dead_in_gossip_info_of_leader_of_elections : ElectionsFixture {
	public when_electing_a_leader_and_best_candidate_node_is_dead_in_gossip_info_of_leader_of_elections() :
		base(NodeFactory(3), NodeFactory(2), NodeFactory(1)) {

		_sut.Handle(new GossipMessage.GossipUpdated(new ClusterInfo(
			MemberInfoFromVNode(_node, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeTwo, _timeProvider.UtcNow, VNodeState.Unknown, true, 0, _epochId, 0),
			MemberInfoFromVNode(_nodeThree, _timeProvider.UtcNow, VNodeState.Unknown, false, 0, _epochId, 0))));

		_sut.Handle(new ElectionMessage.StartElections());
		_sut.Handle(new ElectionMessage.ViewChange(_nodeThree.InstanceId, _nodeThree.HttpEndPoint, 0));

		_sut.Handle(new ElectionMessage.PrepareOk(0, _nodeThree.InstanceId, _nodeThree.HttpEndPoint, 10, 0,
			_epochId, _nodeThree.InstanceId, 0, 0, 0, 0, new ClusterInfo()));

		_publisher.Messages.Clear();
		_sut.Handle(new ElectionMessage.Accept(_nodeTwo.InstanceId, _nodeTwo.HttpEndPoint,
			_nodeThree.InstanceId, _nodeThree.HttpEndPoint, 0));
	}

	[Test]
	public void no_leader_should_be_elected() {
		Assert.AreEqual(0, _publisher.Messages.ToArray().Length);
	}
}
