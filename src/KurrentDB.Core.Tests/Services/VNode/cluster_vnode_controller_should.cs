// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using KurrentDB.Common.Configuration;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.VNode;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.VNode;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class cluster_vnode_controller_should<TLogFormat, TStreamId> {
	private ClusterVNodeOptions _options;
	private ClusterVNode<TStreamId> _node;

	[OneTimeSetUp]
	public void TestFixtureSetUp() {
		_options = new ClusterVNodeOptions()
			.ReduceMemoryUsageForTests()
			.RunInMemory()
			.Insecure();

		_node = new ClusterVNode<TStreamId>(_options, LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory);
	}

	private ClusterVNodeController<TStreamId> CreateSut() {
		return new ClusterVNodeController<TStreamId>(
			new QueueStatsManager(),
			new Trackers(),
			_node.NodeInfo,
			_node.Db,
			new NodeStatusTracker.NoOp(),
			new MetricsConfiguration(),
			_options,
			_node,
			new MessageForwardingProxy(),
			startSubsystems: () => { });
	}

	private static MemberInfoLite Leader() => new() {
		InstanceId = Guid.NewGuid(),
		HttpEndPoint = new DnsEndPoint("leader", 2113),
		ReplicationEndPoint = new DnsEndPoint("leader", 1112),
		ClientHttpEndPoint = new DnsEndPoint("leader", 2113),
		EpochNumber = 0,
	};

	[Test]
	public async Task discard_a_delayed_become_pre_replica_after_becoming_unknown() {
		var sut = CreateSut();
		var preReplicas = new List<SystemMessage.BecomePreReplica>();
		sut.MainBus.Subscribe(new AdHocHandler<SystemMessage.BecomePreReplica>(preReplicas.Add));

		try {
			await Enqueue<SystemMessage.BecomeUnknown>(sut, new SystemMessage.BecomeUnknown(Guid.NewGuid()));

			// the node is told who the leader is and starts replicating from it
			await Enqueue<SystemMessage.BecomePreReplica>(sut, new ElectionMessage.LeaderAppointed(epochNumber: 0, Leader()));
			Assert.That(preReplicas, Has.Count.EqualTo(1));

			// this is the message that VNodeConnectionLost schedules on a timer when the leader connection drops
			var delayed = preReplicas[0];

			// the node returns to Unknown before the timer fires, which clears the leader
			await Enqueue<SystemMessage.BecomeUnknown>(sut, new SystemMessage.BecomeUnknown(Guid.NewGuid()));

			// the delayed message is stale — enqueue it, then fence with a new leader appointment
			// to prove that the stale message was discarded rather than throwing "_leader == null"
			sut.MainQueue.Publish(delayed);
			await Enqueue<SystemMessage.BecomePreReplica>(sut, new ElectionMessage.LeaderAppointed(epochNumber: 0, Leader()));

			// 2 BecomePreReplica dispatches: one from each LeaderAppointed. The stale one was discarded.
			Assert.That(preReplicas, Has.Count.EqualTo(2));
		} finally {
			await ((IQueuedHandler)sut.MainQueue).Stop();
		}
	}

	// Publishes a message to the main queue and waits for a message of type TOutput on the output bus,
	// which indicates the input message (and any inline follow-on) has been processed.
	private static async Task Enqueue<TOutput>(ClusterVNodeController<TStreamId> sut, Message message) where TOutput : Message {
		var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		sut.MainBus.Subscribe(new AdHocHandler<TOutput>(_ => tcs.TrySetResult()));
		sut.MainQueue.Publish(message);
		await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}
}
