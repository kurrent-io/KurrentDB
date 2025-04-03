// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Core.Cluster;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.VNode;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.VNode.InaugurationManagement;

public abstract class InaugurationManagerTests {
	protected readonly Guid _correlationId1 = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
	protected readonly Guid _correlationId2 = Guid.Parse("00000000-0000-0000-0000-0000000000c2");
	protected readonly Guid _correlationId3 = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
	protected readonly int _epochNumber = 15;
	protected readonly MemberInfo _leader =
		MemberInfo.ForVNode(
			default, default, default, default,
			new DnsEndPoint("localhost", default), default, default, default,
			new DnsEndPoint("localhost", default), default, default, default,
			default, default, default, default, default, default, default, default);
	protected readonly long _replicationTarget = 400;
	protected readonly long _indexTarget = 400;

	protected InaugurationManager _sut;
	protected FakePublisher _publisher;
	protected InMemoryCheckpoint _replicationCheckpoint;
	protected InMemoryCheckpoint _indexCheckpoint;

	[SetUp]
	public void SetUp() {
		_publisher = new FakePublisher();
		_replicationCheckpoint = new InMemoryCheckpoint();
		_indexCheckpoint = new InMemoryCheckpoint();
		_sut = new InaugurationManager(_publisher, _replicationCheckpoint, _indexCheckpoint, new NodeStatusTracker.NoOp());
		Given();
	}

	protected abstract void Given();

	protected void When(Message m) {
		_sut.Handle((dynamic)m);
	}

	protected SystemMessage.EpochWritten GenEpoch(int epochNumber) {
		var epoch = new SystemMessage.EpochWritten(new EpochRecord(
						epochPosition: _replicationTarget - 1,
						epochNumber: epochNumber,
						epochId: Guid.NewGuid(),
						prevEpochPosition: _replicationTarget - 20,
						timeStamp: DateTime.UtcNow,
						leaderInstanceId: Guid.NewGuid()));
		return epoch;
	}

	protected void ProgressReplication() {
		_replicationCheckpoint.Write(_replicationTarget / 2);
		_replicationCheckpoint.Flush();
	}

	protected void CompleteReplication() {
		_replicationCheckpoint.Write(_replicationTarget);
		_replicationCheckpoint.Flush();
	}

	protected void ProgressIndexing() {
		_indexCheckpoint.Write(_indexTarget / 2);
		_indexCheckpoint.Flush();
	}

	protected void CompleteIndexing() {
		_indexCheckpoint.Write(_indexTarget);
		_indexCheckpoint.Flush();
	}

	// check that we have reset to initial state, do this by checking we can
	// proceed forward from it
	protected void AssertInitial() {
		Assert.IsEmpty(_publisher.Messages);
		_sut.Handle(new SystemMessage.BecomePreLeader(_correlationId3));
		AssertWaitingForChaser(_correlationId3);
	}

	// check that we have reset to waiting fo chaser, do this by checking we can
	// proceed forward from it
	protected void AssertWaitingForChaser(Guid expectedCorrelationId) {
		Assert.IsEmpty(_publisher.Messages);
		_sut.Handle(new SystemMessage.ChaserCaughtUp(expectedCorrelationId));
		Assert.AreEqual(1, _publisher.Messages.Count);
		Assert.IsInstanceOf<SystemMessage.WriteEpoch>(_publisher.Messages[0]);
	}

	protected void AssertSentBecomeLeader() {
		Assert.AreEqual(1, _publisher.Messages.Count);
		var becomeLeader = AssertEx.IsType<SystemMessage.BecomeLeader>(_publisher.Messages[0]);
		Assert.AreEqual(_correlationId1, becomeLeader.CorrelationId);
		_publisher.Messages.Clear();
		AssertInitial();
	}
}
